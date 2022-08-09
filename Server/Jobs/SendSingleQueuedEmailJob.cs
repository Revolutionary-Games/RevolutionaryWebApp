namespace ThriveDevCenter.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Services;

public class SendSingleQueuedEmailJob
{
    private readonly ILogger<SendSingleQueuedEmailJob> logger;
    private readonly IMailSender mailSender;

    public SendSingleQueuedEmailJob(ILogger<SendSingleQueuedEmailJob> logger, IMailSender mailSender)
    {
        this.logger = logger;
        this.mailSender = mailSender;
    }

    public async Task Execute(MailRequest mailRequest, CancellationToken cancellationToken)
    {
        await mailSender.SendEmail(mailRequest, cancellationToken);
        logger.LogInformation("Sent queued email to {Recipient}", mailRequest.Recipient);
    }
}