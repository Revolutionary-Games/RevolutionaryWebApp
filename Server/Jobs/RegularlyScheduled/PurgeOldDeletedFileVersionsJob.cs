namespace ThriveDevCenter.Server.Jobs.RegularlyScheduled;

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
///   Permanently removes file versions that have been deleted for a while
/// </summary>
[DisableConcurrentExecution(1800)]
public class PurgeOldDeletedFileVersionsJob : IJob
{
    private readonly ILogger<PurgeOldDeletedFileVersionsJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    public PurgeOldDeletedFileVersionsJob(ILogger<PurgeOldDeletedFileVersionsJob> logger, ApplicationDbContext database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.DeleteFileVersionsAfter;

        var versionsToPurge = await database.StorageItemVersions
            .Where(v => v.Deleted && !v.Uploading && v.UpdatedAt < cutoff)
            .ToListAsync(cancellationToken);

        // AsNoTracking can't be used as tests won't pass otherwise (see below for the comment on delete)

        if (versionsToPurge.Count < 1)
            return;

        logger.LogInformation("Purging {Count} storage item versions permanently that were deleted a bit ago",
            versionsToPurge.Count);

        foreach (var version in versionsToPurge)
        {
            logger.LogInformation("Permanently deleting file version {Version} ({Id1}) for item {Id2}", version.Version,
                version.Id, version.StorageItemId);

            jobClient.Enqueue<DeleteStorageItemVersionJob>(x => x.Execute(version.Id, CancellationToken.None));

            if (cancellationToken.IsCancellationRequested)
                break;
        }
    }
}
