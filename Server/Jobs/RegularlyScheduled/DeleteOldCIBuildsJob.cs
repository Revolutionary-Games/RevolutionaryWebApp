namespace ThriveDevCenter.Server.Jobs.RegularlyScheduled;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

[DisableConcurrentExecution(1800)]
public class DeleteOldCIBuildsJob : IJob
{
    private readonly ILogger<DeleteOldCIBuildsJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    private readonly Dictionary<long, int> ciProjectBuildsCount = new();

    public DeleteOldCIBuildsJob(ILogger<DeleteOldCIBuildsJob> logger, ApplicationDbContext database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var deleteCutoff = DateTime.UtcNow - AppInfo.DeleteCIBuildsAfter;

        var buildsToDelete = await database.CiBuilds.Include(b => b.CiJobs).Where(b => b.CreatedAt < deleteCutoff)
            .Where(b => b.CiJobs.All(j => j.OutputPurged)).OrderBy(b => b.CreatedAt).AsNoTracking()
            .ToListAsync(cancellationToken);

        if (buildsToDelete.Count < 1)
            return;

        logger.LogInformation("Starting delete of {Count} old CI builds", buildsToDelete.Count);

        int deleted = 0;

        foreach (var build in buildsToDelete)
        {
            // Do not delete the newest build (only build, thanks to the ordering above) of a project
            if (await GetBuildCountForProject(build.CiProjectId, cancellationToken) < 2)
            {
                logger.LogInformation(
                    "Skip deleting build ({ProjectId}-{BuildId}) for CI project that has only few builds: {ProjectId2}",
                    build.CiProjectId, build.CiBuildId, build.CiProjectId);
                continue;
            }

            DecrementBuildCountForProject(build.CiProjectId);

            // Delete with a background job as that should be much better
            jobClient.Enqueue<DeleteCiBuildJob>(x =>
                x.Execute(build.CiProjectId, build.CiBuildId, CancellationToken.None));

            logger.LogInformation("Queued job to delete CI build {ProjectId}-{BuildId}", build.CiProjectId,
                build.CiBuildId);

            ++deleted;

            if (cancellationToken.IsCancellationRequested)
                break;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            // Don't want to cancel here so that we can preserve the count of things we deleted
            // ReSharper disable MethodSupportsCancellation
            await database.LogEntries.AddAsync(new LogEntry
            {
                Message = $"Deleted {deleted} old CI builds",
            });

            await database.SaveChangesAsync();

            // ReSharper restore MethodSupportsCancellation
        }
    }

    private async Task<int> GetBuildCountForProject(long projectId, CancellationToken cancellationToken)
    {
        if (ciProjectBuildsCount.TryGetValue(projectId, out var alreadyExistingValue))
            return alreadyExistingValue;

        // This code assumes that there'll be just a couple of projects so it is fine to count their jobs one by one
        int count = await database.CiBuilds.CountAsync(b => b.CiProjectId == projectId, cancellationToken);

        ciProjectBuildsCount[projectId] = count;

        return count;
    }

    private void DecrementBuildCountForProject(long projectId)
    {
        ciProjectBuildsCount[projectId] -= 1;
    }
}
