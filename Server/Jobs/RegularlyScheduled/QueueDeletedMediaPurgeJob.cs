namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;
using Pages;
using Utilities;

[DisableConcurrentExecution(1000)]
public class QueueDeletedMediaPurgeJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    public QueueDeletedMediaPurgeJob(ApplicationDbContext database, IBackgroundJobClient jobClient)
    {
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var processedCutoff = now.AddDays(-MediaFileLifecycle.ProcessedRetentionDays);
        var failedCutoff = now.AddDays(-MediaFileLifecycle.FailedRetentionDays);
        var files = database.MediaFiles.Where(file => file.Deleted && (file.PurgeStarted ||
                (file.DeletedAt ?? file.UpdatedAt) <= (file.Processed ? processedCutoff : failedCutoff)))
            .Select(file => new { file.Id, DeletedAt = file.DeletedAt ?? file.UpdatedAt });

        await foreach (var file in files.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            jobClient.Enqueue<PurgeMediaFileJob>(job =>
                job.Execute(file.Id, file.DeletedAt, false, CancellationToken.None));
        }
    }
}
