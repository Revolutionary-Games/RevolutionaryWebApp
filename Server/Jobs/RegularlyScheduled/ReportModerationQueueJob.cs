namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Utilities;

[DisableConcurrentExecution(1000)]
public class ReportModerationQueueJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IConfiguration configuration;
    private readonly IBackgroundJobClient jobs;

    public ReportModerationQueueJob(ApplicationDbContext database, IConfiguration configuration,
        IBackgroundJobClient jobs)
    {
        this.database = database;
        this.configuration = configuration;
        this.jobs = jobs;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["Discord:ModerationQueue"]))
            return;

        var queue = database.UserReports.AsNoTracking().Where(report => report.ResolvedAt == null);
        var count = await queue.CountAsync(cancellationToken);
        if (count == 0)
            return;

        var oldest = await queue.MinAsync(report => report.CreatedAt, cancellationToken);
        var path = new Uri(configuration.GetBaseUrl(), "moderation");
        var message = $"Moderation queue: {count} unresolved reports. " +
            $"Oldest report: {oldest:u}. Review: {path}";
        jobs.Enqueue<SendDiscordWebhookMessageJob>(job =>
            job.Execute("ModerationQueue", message, CancellationToken.None));
    }
}
