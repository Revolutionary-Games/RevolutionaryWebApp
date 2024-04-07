namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Models;
using Utilities;

[DisableConcurrentExecution(100)]
public class DeletePrecompiledObjectVersionIfUploadFailed
{
    private readonly ILogger<DeletePrecompiledObjectVersionIfUploadFailed> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public DeletePrecompiledObjectVersionIfUploadFailed(ILogger<DeletePrecompiledObjectVersionIfUploadFailed> logger,
        NotificationsEnabledDb database, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    /// <summary>
    ///   Handles the full deletion cycle of a precompiled object version. The object must have storage items and their
    ///   versions loaded.
    /// </summary>
    public static void DeletePrecompiledObjectVersion(PrecompiledObjectVersion objectVersion,
        IBackgroundJobClient jobClient)
    {
        if (objectVersion.StoredInItem == null)
            throw new NotLoadedModelNavigationException();

        // Queue the jobs to perform the actions
        foreach (var storageItemVersion in objectVersion.StoredInItem.StorageItemVersions)
        {
            jobClient.Enqueue<DeleteStorageItemVersionJob>(x =>
                x.Execute(storageItemVersion.Id, CancellationToken.None));
        }

        jobClient.Schedule<DeletePrecompiledObjectVersionIfUploadFailed>(x =>
                x.PerformFinalDelete(objectVersion.OwnedById, objectVersion.Version, objectVersion.Platform,
                    objectVersion.Tags, CancellationToken.None),
            TimeSpan.FromSeconds(90));

        // If something was in uploading state, then it can be deleted like normal. Upload path cleaning will take care
        // of the rest. And any exploits will be fixed once there is randomization to the upload paths.

        // We can't finish anything here yet as the StorageItem needs to be deleted at the same time as the symbol
    }

    public static async Task<PrecompiledObjectVersion?> DeletePrecompiledObjectFinal(long ownedById, string version,
        PackagePlatform platform, PrecompiledTag tags, ApplicationDbContext database,
        IBackgroundJobClient jobClient, CancellationToken cancellationToken)
    {
        var objectVersion = await database.PrecompiledObjectVersions.Include(s => s.StoredInItem)
            .ThenInclude(i => i.StorageItemVersions).Where(v =>
                v.OwnedById == ownedById && v.Version == version && v.Platform == platform && v.Tags == tags)
            .FirstOrDefaultAsync(cancellationToken);

        if (objectVersion == null)
            return null;

        if (objectVersion.StoredInItem == null)
            throw new NotLoadedModelNavigationException();

        if (objectVersion.StoredInItem.StorageItemVersions.Count > 0)
            throw new Exception("PrecompiledObjectVersion's storage item still has existing versions");

        // These need to be deleted in this order to not cause a constraint error
        database.PrecompiledObjectVersions.Remove(objectVersion);
        database.StorageItems.Remove(objectVersion.StoredInItem);

        if (objectVersion.StoredInItem.ParentId != null)
        {
            var parentId = objectVersion.StoredInItem.ParentId.Value;
            jobClient.Schedule<CountFolderItemsJob>(x => x.Execute(parentId, CancellationToken.None),
                TimeSpan.FromSeconds(90));
        }

        return objectVersion;
    }

    public async Task Execute(long ownedById, string version, PackagePlatform platform, PrecompiledTag tags,
        CancellationToken cancellationToken)
    {
        var objectVersion = await database.PrecompiledObjectVersions.Include(s => s.StoredInItem)
            .ThenInclude(i => i.StorageItemVersions).Where(v =>
                v.OwnedById == ownedById && v.Version == version && v.Platform == platform && v.Tags == tags)
            .FirstOrDefaultAsync(cancellationToken);

        if (objectVersion == null)
        {
            logger.LogInformation("Not running delete failed upload precompiled object as it doesn't exist");
            return;
        }

        if (objectVersion.Uploaded)
        {
            logger.LogInformation("Precompiled object is uploaded");
            return;
        }

        if (objectVersion.StoredInItem == null)
            throw new NotLoadedModelNavigationException();

        if (DateTime.UtcNow - objectVersion.CreatedAt < TimeSpan.FromSeconds(60))
        {
            throw new Exception("Cannot try to delete a version created less than 60 seconds ago");
        }

        logger.LogWarning("Precompiled object {Identifier} has not been uploaded successfully, deleting it",
            objectVersion.StorageFileName);

        DeletePrecompiledObjectVersion(objectVersion, jobClient);
    }

    public async Task PerformFinalDelete(long ownedById, string version, PackagePlatform platform, PrecompiledTag tags,
        CancellationToken cancellationToken)
    {
        var objectVersion = await DeletePrecompiledObjectFinal(ownedById, version, platform, tags, database, jobClient,
            cancellationToken);

        if (objectVersion == null)
        {
            logger.LogError("PrecompiledObjectVersion disappeared before final delete task could run on it: " +
                "{Id}:{Platform}:{Tags}:{Version}", ownedById, platform, tags, version);
            return;
        }

        logger.LogInformation(
            "Performing final delete PrecompiledObjectVersion {Identifier} as it had not been uploaded " +
            "successfully or it hasn't been downloaded in a long time",
            objectVersion.StorageFileName);

        await database.LogEntries.AddAsync(
            new LogEntry($"Deleted old or failed PrecompiledObjectVersion {objectVersion.StorageFileName}"),
            cancellationToken);

        await database.SaveChangesAsync(cancellationToken);

        // Recalculate the total size with the now deleted item
        jobClient.Enqueue<CountPrecompiledObjectSizeJob>(x => x.Execute(ownedById, CancellationToken.None));
    }
}
