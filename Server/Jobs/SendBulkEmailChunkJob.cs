namespace ThriveDevCenter.Server.Jobs;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using Models;
using Services;

[DisableConcurrentExecution(300)]
public class SendBulkEmailChunkJob
{
    private readonly ILogger<SendBulkEmailChunkJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IMailSender mailSender;

    public SendBulkEmailChunkJob(ILogger<SendBulkEmailChunkJob> logger, ApplicationDbContext database,
        IMailSender mailSender)
    {
        this.logger = logger;
        this.database = database;
        this.mailSender = mailSender;
    }

    public async Task Execute(long bulkId, List<string> recipients, string? replyTo,
        CancellationToken cancellationToken)
    {
        var bulkInfo = await database.SentBulkEmails.FindAsync(new object[] { bulkId }, cancellationToken);

        if (bulkInfo == null)
        {
            logger.LogError("Can't send bulk email {BulkId} to recipients, can't find sent data", bulkId);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Once we start sending we would need a job database write if we need to cancel, so for now we don't
        // allow canceling after starting
        foreach (var recipient in recipients)
        {
            await mailSender.SendEmail(new MailRequest(recipient, bulkInfo.Title)
            {
                ReplyTo = replyTo,
                HtmlBody = bulkInfo.HtmlBody,
                PlainTextBody = bulkInfo.PlainBody,
            }, CancellationToken.None);
        }
    }
}
