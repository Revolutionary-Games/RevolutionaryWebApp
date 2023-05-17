namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

/// <summary>
///   Deletes non-important DevBuilds older than the configured number of days
/// </summary>
[DisableConcurrentExecution(1800)]
public class CleanOldDevBuildsJob : IJob
{
    private readonly ILogger<CleanOldDevBuildsJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    public CleanOldDevBuildsJob(ILogger<CleanOldDevBuildsJob> logger, ApplicationDbContext database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.UnimportantDevBuildKeepDuration;

        var buildsToDelete = await database.DevBuilds
            .Where(b => !b.Important && !b.Keep && !b.BuildOfTheDay && b.UpdatedAt < cutoff)
            .Include(b => b.StorageItem).ThenInclude(s => s!.StorageItemVersions).ThenInclude(v => v.StorageFile)
            .OrderBy(b => b.Id).Take(AppInfo.MaxDevBuildsToCleanAtOnce).ToListAsync(cancellationToken);

        if (buildsToDelete.Count < 1)
            return;

        var resourceStats =
            await database.DeletedResourceStats.FirstOrDefaultAsync(
                r => r.Type == DeletedResourceStats.ResourceType.DevBuild, cancellationToken);

        if (resourceStats == null)
        {
            resourceStats = new DeletedResourceStats(DeletedResourceStats.ResourceType.DevBuild);
            await database.DeletedResourceStats.AddAsync(resourceStats, cancellationToken);
        }

        logger.LogInformation("Cleaning {Count} old DevBuilds", buildsToDelete.Count);

        Exception? problem = null;
        bool doneSomething = false;
        int deleted = 0;

        foreach (var build in buildsToDelete)
        {
            logger.LogInformation("Deleting old DevBuild {Id}", build.Id);

            try
            {
                if (build.StorageItem == null)
                {
                    logger.LogWarning(
                        "The DevBuild to delete does not have a storage item, will delete the build anyway");
                }
                else
                {
                    DeleteStorageItemJob.PerformProperDelete(build.StorageItem, jobClient);
                }

                // TODO: is it possible to end up with broken devbuilds accidentally where a build doesn't get removed
                // but the item delete already happened
                database.DevBuilds.Remove(build);
            }
            catch (Exception e)
            {
                problem = e;
                logger.LogError(e, "Failed to delete DevBuild {Id}", build.Id);

                if (doneSomething)
                    logger.LogInformation("Will save DevBuild deletes that already succeeded before failing");
                break;
            }

            // Track the deleted item data still for overall stats display
            ++resourceStats.ItemCount;
            resourceStats.ItemsExtraAttribute += build.Downloads;

            doneSomething = true;
            ++deleted;

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        if (doneSomething)
        {
            logger.LogDebug("Saving DevBuild clean results (deleted: {Deleted}) to DB", deleted);

            // We want to save anyway, so adding the log message anyway also seems like a good idea
            // ReSharper disable once MethodSupportsCancellation
            await database.LogEntries.AddAsync(new LogEntry
            {
                Message = $"Cleaned old DevBuilds, deleted: {deleted}",
            });

            // We *really* don't want to lose info on what files we have deleted from remote storage
            // ReSharper disable once MethodSupportsCancellation
            await database.SaveChangesAsync();

            jobClient.Enqueue<DeleteUnneededDehydratedObjectsJob>(x => x.Execute(CancellationToken.None));
        }

        if (problem != null)
        {
            throw new AggregateException(problem);
        }
    }
}
