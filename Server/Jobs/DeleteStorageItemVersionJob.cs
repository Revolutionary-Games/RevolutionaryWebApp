namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Utilities;

public class DeleteStorageItemVersionJob
{
    private readonly ILogger<DeleteStorageItemVersionJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly GeneralRemoteStorage remoteStorage;

    public DeleteStorageItemVersionJob(ILogger<DeleteStorageItemVersionJob> logger, NotificationsEnabledDb database,
        GeneralRemoteStorage remoteStorage)
    {
        this.logger = logger;
        this.database = database;
        this.remoteStorage = remoteStorage;
    }

    public async Task Execute(long versionId, CancellationToken cancellationToken)
    {
        var version = await database.StorageItemVersions.Include(v => v.StorageFile)
            .FirstOrDefaultAsync(i => i.Id == versionId, cancellationToken);

        if (version == null)
        {
            logger.LogWarning(
                "Failed to get StorageItemVersion ({VersionId}) for deletion, assuming already deleted", versionId);
            return;
        }

        if (version.StorageFile == null)
            throw new NotLoadedModelNavigationException();

        logger.LogInformation("Deleting remote storage object queued for deletion: {StoragePath}",
            version.StorageFile.StoragePath);

        await remoteStorage.DeleteObject(version.StorageFile.StoragePath);

        database.StorageItemVersions.Remove(version);
        database.StorageFiles.Remove(version.StorageFile);

        // Not cancellable as we have already deleted the remote item at this point
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();

        try
        {
            await remoteStorage.DeleteObject(version.StorageFile.UploadPath);
            logger.LogInformation(
                "Deleted upload path for a remote storage item that was just deleted: {UploadPath}",
                version.StorageFile.UploadPath);
        }
        catch (Exception e)
        {
            logger.LogTrace("Upload path probably didn't exist for the above file, couldn't delete it: {@E}", e);
        }
    }
}
