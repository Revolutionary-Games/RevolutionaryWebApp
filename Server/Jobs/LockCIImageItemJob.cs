namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    [DisableConcurrentExecution(45)]
    public class LockCIImageItemJob
    {
        private readonly ILogger<LockCIImageItemJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public LockCIImageItemJob(ILogger<LockCIImageItemJob> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        public async Task Execute(long itemId, CancellationToken cancellationToken)
        {
            var item = await database.StorageItems.Include(i => i.StorageItemVersions)
                .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

            if (item == null)
            {
                logger.LogError("Failed to get StorageItem ({ItemId}) for CI image locking", itemId);
                return;
            }

            if (item.WriteAccess == FileAccess.Nobody && item.Special)
            {
                logger.LogInformation("Skipping lock on StorageItem ({ItemId}) as it's already in nobody write mode",
                    itemId);
                return;
            }

            // Queue jobs to delete other than the first uploaded version
            var lowestVersion = item.StorageItemVersions.Where(v => !v.Uploading).Min(v => v.Version);

            foreach (var version in item.StorageItemVersions)
            {
                if (version.Version == lowestVersion)
                {
                    if (version.Protected != true || version.Keep != true)
                    {
                        version.Protected = true;
                        version.Keep = true;
                        version.BumpUpdatedAt();
                    }
                }
                else
                {
                    // This version needs to be deleted
                    jobClient.Schedule<DeleteStorageItemVersionJob>(x => x.Execute(version.Id, CancellationToken.None),
                        TimeSpan.FromSeconds(30));
                }
            }

            item.WriteAccess = FileAccess.Nobody;
            item.Special = true;
            item.BumpUpdatedAt();

            // Also mark all parent folders as special so that they can't be deleted
            foreach (var parent in await item.GetParentsRecursively(database))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (parent.Special != true)
                {
                    parent.Special = true;
                    logger.LogInformation("CI image parent folder ({Id}) will be marked as special", parent.Id);
                }
            }

            await database.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Use as CI image has locked writing to StorageItem ({ItemId})", itemId);
        }
    }
}
