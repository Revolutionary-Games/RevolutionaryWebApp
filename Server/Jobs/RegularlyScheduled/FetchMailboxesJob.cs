namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Utilities;
using Hangfire;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Utilities;

/// <summary>
///   Periodically fetches incoming emails from the main notification mailbox to process special commands
///   (like "resume email") and detect bounces.
/// </summary>
[DisableConcurrentExecution(500)]
public class FetchMailboxesJob : IJob
{
    private readonly ILogger<FetchMailboxesJob> logger;
    private readonly IConfiguration configuration;
    private readonly ApplicationDbContext database;

    public FetchMailboxesJob(ILogger<FetchMailboxesJob> logger, IConfiguration configuration,
        ApplicationDbContext database)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.database = database;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task Execute(CancellationToken cancellationToken)
    {
        // Read IMAP configuration (fallback to SMTP host when IMAP host is not specified)
        var imapHost = configuration["Email:ImapHost"] ?? configuration["Email:Host"] ?? string.Empty;
        var imapPort = TryGetInt(configuration["Email:ImapPort"]) ?? 993;
        var imapUseSsl = TryGetBool(configuration["Email:ImapUseSsl"]) ?? true;
        var username = configuration["Email:Username"] ?? configuration["Email:FromAddress"] ?? string.Empty;
        var password = configuration["Email:Password"] ?? string.Empty;

        if (string.IsNullOrEmpty(imapHost) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            logger.LogInformation("IMAP not configured, skipping FetchMailboxesJob");
            return;
        }

        using var client = new ImapClient();

        try
        {
            await client.ConnectAsync(imapHost, imapPort, imapUseSsl, cancellationToken);
            await client.AuthenticateAsync(username, password, cancellationToken);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            // Delete very old messages (older than one year)
            var cutoff = DateTime.UtcNow.AddYears(-1);
            var oldUids = await inbox.SearchAsync(SearchQuery.DeliveredBefore(cutoff), cancellationToken);
            if (oldUids?.Count > 0)
            {
                await inbox.AddFlagsAsync(oldUids, MessageFlags.Deleted, true, cancellationToken);
                await inbox.ExpungeAsync(cancellationToken);
            }

            // Process unread messages
            var unread = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);

            foreach (var uid in unread)
            {
                try
                {
                    var message = await inbox.GetMessageAsync(uid, cancellationToken);

                    // Mark as read (seen) first, regardless of processing outcome
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);

                    await ProcessMessageAsync(message, cancellationToken);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Failed processing an incoming email UID {Uid}", uid);
                }
            }

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while fetching mailboxes");
        }
    }

    private static bool IsBounce(MimeKit.MimeMessage message)
    {
        // Subject-based heuristics
        var subject = message.Subject ?? string.Empty;
        if (subject.Contains("Undelivered Mail Returned to Sender", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("Delivery Status Notification", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("Mail delivery failed", StringComparison.OrdinalIgnoreCase) ||
            subject.Contains("Delivery failure", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // MIME report type
        if (message.Body is MimeKit.MultipartReport report &&
            report.ReportType?.Contains("delivery-status", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        // Presence of X-Failed-Recipients header
        if (message.Headers.Contains("X-Failed-Recipients"))
        {
            return true;
        }

        return false;
    }

    private static string? GetFailedRecipient(MimeKit.MimeMessage message)
    {
        if (message.Headers.Contains("X-Failed-Recipients"))
            return message.Headers["X-Failed-Recipients"];

        // Try to parse from delivery-status part
        if (message.Body is MimeKit.MultipartReport report)
        {
            foreach (var part in report)
            {
                if (part is MimeKit.MessageDeliveryStatus status)
                {
                    foreach (var group in status.StatusGroups)
                    {
                        if (group.Contains("Final-Recipient"))
                            return group["Final-Recipient"];
                        if (group.Contains("Original-Recipient"))
                            return group["Original-Recipient"];
                    }
                }
            }
        }

        return null;
    }

    private static int? TryGetInt(string? value)
    {
        return int.TryParse(value, out var v) ? v : null;
    }

    private static bool? TryGetBool(string? value)
    {
        return bool.TryParse(value, out var v) ? v : null;
    }

    private async Task ProcessMessageAsync(MimeKit.MimeMessage message, CancellationToken ct)
    {
        var subject = message.Subject ?? string.Empty;
        var fromAddress = message.From.Mailboxes.FirstOrDefault()?.Address;
        if (string.IsNullOrEmpty(fromAddress))
        {
            logger.LogDebug("Skipping message without a sender address");
            return;
        }

        // Heuristic bounce detection
        if (IsBounce(message))
        {
            // Try to get the failed recipient if possible
            var failed = GetFailedRecipient(message) ?? fromAddress;
            logger.LogWarning("Detected bounced email notification for {Address}", failed);
            return;
        }

        if (subject.StartsWith("resume email", StringComparison.OrdinalIgnoreCase))
        {
            await ResumeEmailForAddressAsync(fromAddress, ct);
            return;
        }

        // Other subjects are currently ignored
        logger.LogInformation("Ignoring incoming email with subject '{Subject}'", subject);
    }

    private async Task ResumeEmailForAddressAsync(string address, CancellationToken cancellation)
    {
        var normalized = Normalization.NormalizeEmail(address);

        // Prefer existing user accounts; user presence overrides any direct preferences
        var user = await database.Users
            .Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, cancellation);

        if (user != null)
        {
            var prefs = user.EmailPreferences ?? new Models.Emails.UserEmailPreferences { User = user };

            // Ensure critical emails can get through
            prefs.DisableAllEmails = false;
            prefs.AllowPasswordReset = true;
            prefs.AllowConfirmEmail = true;
            prefs.AllowSiteAnnouncement = true;

            if (user.EmailPreferences == null)
                database.UserEmailPreferences.Add(prefs);

            await database.SaveChangesAsync(cancellation);
            logger.LogInformation("Resumed essential emails for user {UserId} ({Email})", user.Id, address);
            await database.LogEntries.AddAsync(new LogEntry("Resumed essential emails for user")
            {
                TargetUserId = user.Id,
            }, cancellation);
            await database.SaveChangesAsync(cancellation);
            return;
        }

        // Fall back to direct email preferences
        var direct = await database.DirectEmailPreferences
            .FirstOrDefaultAsync(d => d.NormalizedEmail == normalized, cancellation);

        direct ??= await database.DirectEmailPreferences
            .FirstOrDefaultAsync(d => d.Email == address, cancellation);

        if (direct == null)
        {
            logger.LogInformation("No email preferences for address that wants to resume, so there can't be a block!");
            return;
        }

        direct.DisableAllEmails = false;
        direct.AllowPasswordReset = true;
        direct.AllowConfirmEmail = true;
        direct.AllowSiteAnnouncement = true;

        await database.LogEntries.AddAsync(
            new LogEntry($"Resumed essential emails for email address {address.Truncate(50)}"), cancellation);

        await database.SaveChangesAsync(cancellation);
        logger.LogInformation("Resumed essential emails for external address {Email}", address);
    }
}
