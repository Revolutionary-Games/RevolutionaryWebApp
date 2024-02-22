namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

[DisableConcurrentExecution(1800)]
public class DeleteOldCIJobOutputJob : IJob
{
    private readonly ILogger<DeleteOldCIJobOutputJob> logger;
    private readonly NotificationsEnabledDb database;

    public DeleteOldCIJobOutputJob(ILogger<DeleteOldCIJobOutputJob> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var successfulDeleteCutoff = DateTime.UtcNow - AppInfo.DeleteSuccessfulJobLogsAfter;
        var failedDeleteCutoff = DateTime.UtcNow - AppInfo.DeleteFailedJobLogsAfter;

        var jobsToClear = await database.CiJobs.Include(j => j.CiJobOutputSections).Where(j =>
                !j.OutputPurged && ((j.Succeeded && j.CreatedAt < successfulDeleteCutoff) ||
                    (!j.Succeeded && j.CreatedAt < failedDeleteCutoff))).OrderBy(j => j.CreatedAt)
            .Take(AppInfo.MaxCiJobOutputsToDeleteAtOnce).ToListAsync(cancellationToken);

        if (jobsToClear.Count < 1)
            return;

        if (jobsToClear.Count >= AppInfo.MaxCiJobOutputsToDeleteAtOnce)
        {
            logger.LogWarning(
                "Too many jobs to delete output sections of at once, hopefully the daily average build count " +
                "is low enough to eventually delete all");
        }

        logger.LogInformation("Clearing build output in {Count} old CI jobs", jobsToClear.Count);

        foreach (var job in jobsToClear)
        {
            if (job.CiJobOutputSections.Count > 0)
            {
                logger.LogInformation(
                    "Deleting the {Count} build output sections of job {CiProjectId}-{CiBuildId}-{CiJobId}",
                    job.CiJobOutputSections.Count, job.CiProjectId, job.CiBuildId, job.CiJobId);

                database.CiJobOutputSections.RemoveRange(job.CiJobOutputSections);
                job.CiJobOutputSections.Clear();
            }
            else
            {
                logger.LogWarning("CI job {CiProjectId}-{CiBuildId}-{CiJobId} doesn't have output sections to purge",
                    job.CiProjectId, job.CiBuildId, job.CiJobId);
            }

            job.OutputPurged = true;

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
