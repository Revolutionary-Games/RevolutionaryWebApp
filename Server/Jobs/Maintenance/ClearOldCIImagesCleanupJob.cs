namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Part 2 of the ClearOldCIImages maintenance operation. Runs after a 90-day delay
///   to delete CI image files that remained admin-writable (i.e. not used by CI which
///   would have locked them) and haven’t been updated in the past week. This is a
///   standalone Hangfire job and not tied to a maintenance operation instance.
/// </summary>
[DisableConcurrentExecution(1800)]
public class ClearOldCIImagesCleanupJob
{
    private readonly ILogger<ClearOldCIImagesCleanupJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public ClearOldCIImagesCleanupJob(ILogger<ClearOldCIImagesCleanupJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var imagesFolder = await StorageItem.FindByPath(database, "CI/Images");

        if (imagesFolder == null)
        {
            await database.LogEntries.AddAsync(new LogEntry("No CI/Images folder found (cannot perform cleanup)"),
                cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            return;
        }

        // Collect all descendants (BFS)
        var toVisit = new Queue<long>();
        toVisit.Enqueue(imagesFolder.Id);

        var candidateIds = new List<long>();

        while (toVisit.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parentId = toVisit.Dequeue();

            var children = await database.StorageItems
                .Where(i => i.ParentId == parentId)
                .Select(i => new { i.Id, i.Ftype })
                .ToListAsync(cancellationToken);

            foreach (var child in children)
            {
                if (child.Ftype == FileType.Folder)
                {
                    toVisit.Enqueue(child.Id);
                }
                else
                {
                    candidateIds.Add(child.Id);
                }
            }
        }

        var weekCutoff = DateTime.UtcNow - TimeSpan.FromDays(7);

        // Fetch candidates with versions for deletion
        var items = await database.StorageItems
            .Include(i => i.StorageItemVersions)
            .Where(i => candidateIds.Contains(i.Id) && !i.Deleted && i.WriteAccess == FileAccess.OwnerOrAdmin &&
                i.UpdatedAt < weekCutoff)
            .ToListAsync(cancellationToken);

        var deleted = 0;

        foreach (var item in items)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                DeleteStorageItemJob.PerformProperDelete(item, jobClient);
                logger.LogInformation("Enqueued deletion for CI image StorageItem {Name} ({Id})", item.Name, item.Id);
                ++deleted;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to queue deletion for CI image StorageItem {Id}", item.Id);
            }
        }

        // Persist log entry about the results (and any updated items)
        // Do not cancel any more as we have deleted things!
        await database.LogEntries.AddAsync(
            new LogEntry($"CI cleanup: deleted {deleted} CI image file(s) (unused after 90 days)."),
            CancellationToken.None);

        await database.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation("ClearOldCIImages cleanup phase queued deletion for {Count} file(s).", deleted);
    }
}
