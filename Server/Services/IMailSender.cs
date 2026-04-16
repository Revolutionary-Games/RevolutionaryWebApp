namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Jobs;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using Utilities;

public interface IMailSender
{
    public bool Configured { get; }

    public Task SendEmail(MailRequest request, CancellationToken cancellationToken);
}

public interface IMailQueue : IMailSender
{
}

/// <summary>
///   Base holder for email configuration and basic helpers. Does not depend on a database.
/// </summary>
public abstract class BaseEmailConfig
{
    protected readonly string fromAddress;
    protected readonly string senderName;
    protected readonly string emailPassword;
    protected readonly string host;
    protected readonly short port;
    protected readonly bool requireTls;

    protected BaseEmailConfig(IConfiguration configuration)
    {
        fromAddress = configuration["Email:FromAddress"] ?? string.Empty;
        senderName = configuration["Email:Name"] ?? string.Empty;
        emailPassword = configuration["Email:Password"] ?? string.Empty;
        host = configuration["Email:Host"] ?? string.Empty;
        port = Convert.ToInt16(configuration["Email:Port"]);
        requireTls = Convert.ToBoolean(configuration["Email:RequireTls"]);

        if (string.IsNullOrEmpty(fromAddress) || string.IsNullOrEmpty(senderName) || string.IsNullOrEmpty(host) ||
            port == 0)
        {
            Configured = false;
            return;
        }

        Configured = true;
    }

    public bool Configured { get; }

    protected void ThrowIfNotConfigured()
    {
        if (!Configured)
            throw new InvalidOperationException("Email is not configured");
    }
}

public class MailSender : BaseEmailConfig, IMailSender
{
    private readonly ILogger<MailSender> logger;
    private readonly ApplicationDbContext database;

    public MailSender(ILogger<MailSender> logger, IConfiguration configuration, ApplicationDbContext database)
        : base(configuration)
    {
        this.logger = logger;
        this.database = database;
    }

    public virtual async Task SendEmail(MailRequest request, CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        // Check recipient preferences before doing any network operations
        try
        {
            var isAllowed = await EmailHelpers.IsAllowedAsync(database, request, cancellationToken);
            if (!isAllowed)
            {
                logger.LogInformation(
                    "Not sending email to {Recipient} because preferences disallow category {Category}",
                    request.Recipient, request.Category);
                return;
            }
        }
        catch (Exception e)
        {
            // Fail-safe: if preference lookup errors, log and proceed to avoid blocking critical flows
            logger.LogWarning(e, "Failed to check email preferences for {Recipient}, proceeding to send",
                request.Recipient);
        }

        var sender = MailboxAddress.Parse(fromAddress);
        sender.Name = senderName;

        var builder = new BodyBuilder { HtmlBody = request.HtmlBody, TextBody = request.PlainTextBody };

        if (request.Attachments != null)
        {
            foreach (var attachment in request.Attachments)
            {
                builder.Attachments.Add(attachment.Filename, Encoding.UTF8.GetBytes(attachment.Content),
                    ContentType.Parse(attachment.MimeType));
            }
        }

        var email = new MimeMessage
        {
            Sender = sender,
            Subject = request.Subject,
            Body = builder.ToMessageBody(),
        };

        if (!string.IsNullOrEmpty(request.Bcc))
            email.Bcc.Add(MailboxAddress.Parse(request.Bcc));

        if (!string.IsNullOrEmpty(request.Cc))
            email.Cc.Add(MailboxAddress.Parse(request.Cc));

        if (!string.IsNullOrEmpty(request.ReplyTo))
            email.ReplyTo.Add(MailboxAddress.Parse(request.ReplyTo));

        email.From.Add(sender);

        email.To.Add(MailboxAddress.Parse(request.Recipient));

        using var smtp = new SmtpClient();

        SecureSocketOptions connectMode = SecureSocketOptions.StartTls;

        if (!requireTls)
            connectMode = SecureSocketOptions.StartTlsWhenAvailable;

        // When using ssl port, switch to SSL mode
        if (port == 465)
            connectMode = SecureSocketOptions.SslOnConnect;

        await smtp.ConnectAsync(host, port, connectMode, cancellationToken);

        // Only authenticate if we have a password set
        if (!string.IsNullOrEmpty(emailPassword))
            await smtp.AuthenticateAsync(fromAddress, emailPassword, cancellationToken);

        logger.LogInformation("Sending email to {Recipient} with category {Category}", request.Recipient,
            request.Category);
        await smtp.SendAsync(email, cancellationToken);

        // If this is canceled here, then it might be possible that a single email is sent twice, but should be
        // very rare
        await smtp.DisconnectAsync(true, cancellationToken);
    }
}

/// <summary>
///   Sends mails from a background operation
/// </summary>
public class MailToQueueSender : BaseEmailConfig, IMailQueue
{
    private readonly IBackgroundJobClient jobClient;

    public MailToQueueSender(IConfiguration configuration, IBackgroundJobClient jobClient) : base(configuration)
    {
        this.jobClient = jobClient;
    }

    public Task SendEmail(MailRequest request, CancellationToken cancellationToken)
    {
        ThrowIfNotConfigured();

        jobClient.Enqueue<SendSingleQueuedEmailJob>(x => x.Execute(request, CancellationToken.None));
        return Task.CompletedTask;
    }
}

public class MailRequest
{
    public MailRequest(string recipient, string subject, EmailReason category)
    {
        Recipient = recipient;
        Subject = subject;
        Category = category;
    }

    [Required]
    public string Recipient { get; set; }

    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public string? ReplyTo { get; set; }

    [Required]
    public string Subject { get; set; }

    public string? HtmlBody { get; set; }
    public string? PlainTextBody { get; set; }

    public List<MailAttachment>? Attachments { get; set; }

    /// <summary>
    ///   Email category for applying recipient preferences
    /// </summary>
    [Required]
    public EmailReason Category { get; set; }

    /// <summary>
    ///   Optional target user id if available (for direct user preference lookup)
    /// </summary>
    public long? RecipientUserId { get; set; }
}

public class MailAttachment
{
    public MailAttachment(string filename, string content)
    {
        Filename = filename;
        Content = content;
    }

    [Required]
    public string Filename { get; set; }

    /// <summary>
    ///   Content of the attachment. Needs to be utf8 encoded for now
    /// </summary>
    [Required]
    public string Content { get; set; }

    public string MimeType { get; set; } = "plain/text";
}
