namespace ThriveDevCenter.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class SendCrashReportWatcherNotificationsJob
{
    private readonly ILogger<SendCrashReportWatcherNotificationsJob> logger;

    public SendCrashReportWatcherNotificationsJob(ILogger<SendCrashReportWatcherNotificationsJob> logger)
    {
        this.logger = logger;
    }

    public Task Execute(long reportId, string whatChanged, CancellationToken cancellationToken)
    {
        logger.LogInformation("TODO: implement email notifications for crash reports");
        return Task.CompletedTask;
    }
}