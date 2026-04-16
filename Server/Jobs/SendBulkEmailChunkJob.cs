namespace RevolutionaryWebApp.Server.Jobs;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using Services;
using Utilities;

[DisableConcurrentExecution(300)]
public class SendBulkEmailChunkJob
{
    private readonly ILogger<SendBulkEmailChunkJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IMailSender mailSender;
    private readonly IConfiguration configuration;
    private readonly ITimeLimitedDataProtector timeLimitedProtector;

    public SendBulkEmailChunkJob(ILogger<SendBulkEmailChunkJob> logger, ApplicationDbContext database,
        IMailSender mailSender, IConfiguration configuration, IDataProtectionProvider dataProtectionProvider)
    {
        this.logger = logger;
        this.database = database;
        this.mailSender = mailSender;
        this.configuration = configuration;
        timeLimitedProtector = dataProtectionProvider.CreateProtector(EmailPreferenceToken.ProtectionPurpose)
            .ToTimeLimitedDataProtector();
    }

    public async Task Execute(long bulkId, List<string> recipients, string? replyTo,
        CancellationToken cancellationToken)
    {
        var bulkInfo = await database.SentBulkEmails.FindAsync([bulkId], cancellationToken);

        if (bulkInfo == null)
        {
            logger.LogError("Can't send bulk email {BulkId} to recipients, can't find sent data", bulkId);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Once we start sending, we would need a job database writing if we need to cancel, so for now we don't
        // allow cancelling after starting
        foreach (var recipient in recipients)
        {
            // TODO: allow email configuration per SentBulkEmail object
            var category = EmailReason.SiteAnnouncement;

            // Append standardized footer with manage/unsubscribe info. As bulk emails can be user-authored,
            // we only append the footer without altering the original content.
            var withFooter = await EmailHelpers.GenerateFooterAsync(database, timeLimitedProtector,
                configuration, recipient, category, null, bulkInfo.HtmlBody, bulkInfo.PlainBody, null,
                CancellationToken.None);

            var request = new MailRequest(recipient, bulkInfo.Title, category)
            {
                ReplyTo = replyTo,
                HtmlBody = withFooter.Html,
                PlainTextBody = withFooter.Plain,
            };

            // The main sender does email send filtering
            await mailSender.SendEmail(request, CancellationToken.None);
        }
    }
}
