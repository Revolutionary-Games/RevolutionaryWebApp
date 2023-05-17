namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Deletes just a <see cref="StorageItem"/> to be used in conjunction with
///   <see cref="DeleteStorageItemVersionJob"/> so that this is queued to run a little bit later to clean the
///   parent object after the remote storage deletes have succeeded.
/// </summary>
public class DeleteStorageItemJob
{
    private readonly ILogger<DeleteStorageItemJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public DeleteStorageItemJob(ILogger<DeleteStorageItemJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    /// <summary>
    ///   Properly deletes a <see cref="StorageItem"/>
    /// </summary>
    /// <param name="item">
    ///   The item to delete. Note that the item versions need to be included in the item when it was fetched
    ///   from the database.
    /// </param>
    /// <param name="jobClient">Where to queue the necessary background jobs</param>
    public static void PerformProperDelete(StorageItem item, IBackgroundJobClient jobClient)
    {
        foreach (var storageItemVersion in item.StorageItemVersions)
        {
            jobClient.Enqueue<DeleteStorageItemVersionJob>(x =>
                x.Execute(storageItemVersion.Id, CancellationToken.None));
        }

        jobClient.Schedule<DeleteStorageItemJob>(x => x.ExecutePrivate(item.Id, CancellationToken.None),
            TimeSpan.FromSeconds(60));
    }

    // This is only public as Hangfire will not execute private methods
    public async Task ExecutePrivate(long itemId, CancellationToken cancellationToken)
    {
        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

        if (item == null)
        {
            logger.LogWarning(
                "Failed to get StorageItem ({ItemId}) for deletion, assuming already deleted", itemId);
            return;
        }

        var children = item.StorageItemVersions.Count;

        if (children > 0)
        {
            throw new InvalidOperationException(
                $"Cannot delete a storage item that still has {children} version(s)");
        }

        database.StorageItems.Remove(item);
        await database.SaveChangesAsync(cancellationToken);

        // Requeue counting the parent folder's items job
        if (item.ParentId != null)
        {
            var parentId = item.ParentId.Value;
            jobClient.Enqueue<CountFolderItemsJob>(x => x.Execute(parentId, CancellationToken.None));
        }
    }
}
