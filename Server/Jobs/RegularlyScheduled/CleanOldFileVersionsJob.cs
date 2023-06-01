namespace ThriveDevCenter.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

/// <summary>
///   Deletes old file versions that have existed for a while, and only applies to files with many versions
/// </summary>
[DisableConcurrentExecution(1800)]
public class CleanOldFileVersionsJob : IJob
{
    private readonly ILogger<CleanOldFileVersionsJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    public CleanOldFileVersionsJob(ILogger<CleanOldFileVersionsJob> logger, ApplicationDbContext database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.DeleteFileVersionsAfter;

        // Find items where there are so many versions as to potentially delete

        // TODO: can something be done where old versions of a file are much larger than the new ones?
        // To avoid an "exploit" where multiple big versions can be uploaded and then a small one to keep many of the
        // big old versions
        var itemsWithManyVersions = await database.StorageItems.Include(i => i.StorageItemVersions)
            .ThenInclude(v => v.StorageFile)
            .Where(i => i.Size != null && !i.Important && !i.Special && i.Ftype == FileType.File).Where(i =>
                (i.StorageItemVersions.Count > 1 && i.Size > AppInfo.LargeFileSizeVersionsKeepLimit) ||
                (i.StorageItemVersions.Count > 3 && i.Size > AppInfo.MediumFileSizeVersionsKeepLimit) ||
                (i.StorageItemVersions.Count > 6 && i.Size > AppInfo.SmallFileSizeVersionsKeepLimit) ||
                i.StorageItemVersions.Count > 9)
            .Where(i => i.StorageItemVersions.Any(v => !v.Uploading && v.UpdatedAt < cutoff && !v.Keep && !v.Protected))
            .ToListAsync(cancellationToken);

        // AsNoTracking can't be used as tests won't pass otherwise (see below for the comment on delete)

        if (itemsWithManyVersions.Count < 1)
            return;

        logger.LogInformation("Checking {Count} storage items with many versions where some can probably be cleared",
            itemsWithManyVersions.Count);

        foreach (var item in itemsWithManyVersions)
        {
            // Use the exact size as this is easy to use here, and maybe a bit too excessively expensive in the DB
            // query
            var maxSize = item.StorageItemVersions.Max(v => v.StorageFile?.Size ?? 0);

            int versionsToKeep;

            if (maxSize > AppInfo.LargeFileSizeVersionsKeepLimit)
            {
                versionsToKeep = 1;
            }
            else if (maxSize > AppInfo.MediumFileSizeVersionsKeepLimit)
            {
                versionsToKeep = 3;
            }
            else if (maxSize > AppInfo.SmallFileSizeVersionsKeepLimit)
            {
                versionsToKeep = 6;
            }
            else
            {
                versionsToKeep = 9;
            }

            // TODO: don't count uploading items in the current count
            int versionCount = item.StorageItemVersions.Count;

            if (versionCount <= versionsToKeep)
            {
                // Turns out this does not have too many items
                continue;
            }

            // Sort to go in the order we should delete stuff in
            foreach (var version in item.StorageItemVersions.OrderBy(v => v.UpdatedAt))
            {
                // Stop when enough deleted
                if (versionCount <= versionsToKeep)
                    break;

                // Don't delete new items (if there are some other conditions causing even newer items to be unable
                // to be deleted)
                if (version.UpdatedAt > cutoff)
                    continue;

                // Can't delete items that are uploading, locked etc.
                if (version.Keep || version.Protected || version.Uploading)
                    continue;

                logger.LogInformation(
                    "Deleting file version {Version} ({Id1}) for item {Name} ({Id2}) as this file has many versions",
                    version.Version, version.Id, item.Name, item.Id);

                jobClient.Enqueue<DeleteStorageItemVersionJob>(x => x.Execute(version.Id, CancellationToken.None));
                --versionCount;

                // Even though we don't save the item status to the DB we still set the deleted flag for unit tests to
                // be easier to make
                version.Deleted = true;

                if (cancellationToken.IsCancellationRequested)
                    break;
            }

            if (versionCount > versionsToKeep)
            {
                logger.LogWarning(
                    "Could not find enough versions to delete in item {Name} ({Id2}) to get under limit of {ToKeep}",
                    item.Name, item.Id, versionsToKeep);
            }

            if (cancellationToken.IsCancellationRequested)
                break;
        }
    }
}
