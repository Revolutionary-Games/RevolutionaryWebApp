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
using RevolutionaryWebApp.Shared.Models.Enums;
using Services;
using SharedBase.Utilities;
using StackExchange.Redis;

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
    private readonly IMailQueue mailQueue;
    private readonly IConnectionMultiplexer redis;

    public FetchMailboxesJob(ILogger<FetchMailboxesJob> logger, IConfiguration configuration,
        ApplicationDbContext database, IMailQueue mailQueue, IConnectionMultiplexer redis)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.database = database;
        this.mailQueue = mailQueue;
        this.redis = redis;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task Execute(CancellationToken cancellationToken)
    {
        // Load mailbox configuration (NotificationsReply seeded with id = 1)
        var mailbox = await database.Mailboxes.FirstOrDefaultAsync(m => m.Id == 1, cancellationToken);

        if (mailbox == null)
        {
            logger.LogWarning("No mailbox configuration found (Id=1). Skipping fetch.");
            return;
        }

        if (mailbox.Disabled)
        {
            logger.LogInformation("Mailbox '{Name}' is disabled, skipping fetch.", mailbox.Name);
            return;
        }

        // Read IMAP configuration (fallback to SMTP host when IMAP host is not specified)
        var imapHost = configuration["Email:ImapHost"] ?? configuration["Email:Host"] ?? string.Empty;
        var imapPort = TryGetInt(configuration["Email:ImapPort"]) ?? 993;
        var imapUseSsl = TryGetBool(configuration["Email:ImapUseSsl"]) ?? true;

        // Prefer credentials from the mailbox when provided; otherwise fall back to app configuration
        // Primary mailbox never uses credentials from the database
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

            // This should always be available if authentication succeeded
            var inbox = client.Inbox ?? throw new InvalidOperationException("No inbox available");
            await inbox.OpenAsync(FolderAccess.ReadWrite, cancellationToken);

            // Delete very old messages (older than one year) at most once per day per mailbox
            var now = DateTime.UtcNow;
            var shouldClean = !mailbox.LastCleanUtc.HasValue ||
                (now - mailbox.LastCleanUtc.Value) >= TimeSpan.FromDays(1);
            if (shouldClean)
            {
                var cutoff = now.AddYears(-1);
                var oldUids = await inbox.SearchAsync(SearchQuery.DeliveredBefore(cutoff), cancellationToken);
                if (oldUids?.Count > 0)
                {
                    await inbox.AddFlagsAsync(oldUids, MessageFlags.Deleted, true, cancellationToken);
                    await inbox.ExpungeAsync(cancellationToken);
                    logger.LogInformation("Mailbox cleanup removed {Count} messages older than {Cutoff:u}",
                        oldUids.Count, cutoff);
                }

                mailbox.LastCleanUtc = now;

                // Persist the clean-up timestamp immediately
                try
                {
                    await database.SaveChangesAsync(cancellationToken);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to update LastCleanUtc for mailbox '{Name}'", mailbox.Name);
                }
            }

            // Process unread messages
            var unread = await inbox.SearchAsync(SearchQuery.NotSeen, cancellationToken);
            DateTime? newestReceived = null;

            foreach (var uid in unread)
            {
                try
                {
                    var message = await inbox.GetMessageAsync(uid, cancellationToken);

                    // Mark as read (seen) first, regardless of processing outcome
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, cancellationToken);

                    await ProcessMessageAsync(message, cancellationToken);

                    // Track the newest received time we observe
                    var candidate = message.Date.UtcDateTime;
                    if (newestReceived == null || candidate > newestReceived.Value)
                        newestReceived = candidate;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Failed processing an incoming email UID {Uid}", uid);
                }
            }

            // Update mailbox statistics
            mailbox.LastReadEmailUtc = now;
            if (newestReceived.HasValue)
            {
                // Only move forward in time
                if (!mailbox.LastReceivedEmailUtc.HasValue || newestReceived.Value > mailbox.LastReceivedEmailUtc.Value)
                    mailbox.LastReceivedEmailUtc = newestReceived.Value;
            }

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to update mailbox statistics for '{Name}'", mailbox.Name);
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

            // Record / update bounce info and if threshold met, schedule handling job
            try
            {
                var normalized = Normalization.NormalizeEmail(failed);

                var bounce = await database.EmailBounces
                    .FirstOrDefaultAsync(b => b.Email == failed, ct);

                bounce ??= await database.EmailBounces
                    .FirstOrDefaultAsync(b => b.NormalizedEmail == normalized, ct);

                var now = DateTime.UtcNow;

                if (bounce == null)
                {
                    bounce = new EmailBounce
                    {
                        Email = failed,
                        NormalizedEmail = normalized,
                        OutstandingBounces = 1,
                        FirstBounceUtc = now,
                        LastBounceUtc = now,
                        DisabledBySystem = false,
                        BackoffWeeks = 0,
                    };

                    await database.EmailBounces.AddAsync(bounce, ct);
                }
                else
                {
                    // Keep the latest seen exact casing of the address
                    bounce.Email = failed;
                    bounce.NormalizedEmail = normalized;
                    bounce.OutstandingBounces += 1;
                    if (bounce.OutstandingBounces <= 1)
                        bounce.FirstBounceUtc = now;
                    bounce.LastBounceUtc = now;
                }

                await database.SaveChangesAsync(ct);

                if (bounce.OutstandingBounces >= 3)
                {
                    BackgroundJob.Schedule<BounceHandlingJob>(
                        j => j.HandleAsync(bounce.NormalizedEmail, CancellationToken.None),
                        TimeSpan.FromMinutes(1));
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to upsert email bounce information");
            }

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
            await SendResumeConfirmationAsync(address, cancellation);
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
        await SendResumeConfirmationAsync(address, cancellation);
    }

    private async Task SendResumeConfirmationAsync(string address, CancellationToken cancellation)
    {
        try
        {
            var db = redis.GetDatabase();
            var normalized = Normalization.NormalizeEmail(address);
            var key = $"email:resume:sent:{normalized}";

            // Rate limit: if a confirmation was sent within the last hour, skip sending another
            var alreadySent = await db.StringGetAsync(key);
            if (alreadySent.HasValue)
            {
                logger.LogDebug("Skipping resume confirmation email to {Email} due to rate limit", address);
                return;
            }

            // Set the flag with 1-hour expiry
            await db.StringSetAsync(key, "1", TimeSpan.FromHours(1));

            var subject = "Emails resumed";
            var explanation =
                "You requested emails to be resumed. The most important messages from the website " +
                "(like password resets and confirmation emails) are now allowed to be sent to this address.";

            var htmlBody = "<p>" + System.Net.WebUtility.HtmlEncode(explanation) + "</p>";
            var plainBody = explanation;

            // We don't use footer here as the user shouldn't be expected to immediately re-cancel the messages
            var request = new MailRequest(address, subject, EmailReason.SiteAnnouncement)
            {
                HtmlBody = htmlBody,
                PlainTextBody = plainBody,
            };

            await mailQueue.SendEmail(request, cancellation);
        }
        catch (Exception e)
        {
            // Don't fail the mailbox processing because of confirmation email problems
            logger.LogError(e, "Failed to send resume confirmation email to {Email}", address);
        }
    }
}
