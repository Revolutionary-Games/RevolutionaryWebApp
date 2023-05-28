namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;

/// <summary>
///   Deletes failed file version uploads after a day
/// </summary>
[DisableConcurrentExecution(1800)]
public class DeleteFailedItemVersionUploadsJob : IJob
{
    private readonly ILogger<DeleteFailedItemVersionUploadsJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IGeneralRemoteStorage remoteStorage;

    public DeleteFailedItemVersionUploadsJob(ILogger<DeleteFailedItemVersionUploadsJob> logger,
        ApplicationDbContext database,
        IGeneralRemoteStorage remoteStorage)
    {
        this.logger = logger;
        this.database = database;
        this.remoteStorage = remoteStorage;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.DeleteFailedVersionUploadAfter;

        var versionsToDelete = await database.StorageItemVersions.Include(v => v.StorageFile)
            .Where(v => v.Uploading && v.CreatedAt < cutoff).ToListAsync(cancellationToken);

        if (versionsToDelete.Count < 1)
        {
            logger.LogInformation("No failed version uploads to delete found");
            return;
        }

        foreach (var version in versionsToDelete)
        {
            logger.LogInformation(
                "Deleting storage item {Id} version {Version} that failed to upload (started: {CreatedAt})",
                version.StorageItemId, version.Version, version.CreatedAt);

            // This is really just for the tests to easily check things, this is not actually saved in the DB as the
            // entire entity is deleted from there
            version.Deleted = true;

            if (version.StorageFile != null)
            {
                try
                {
                    await remoteStorage.DeleteObject(version.StorageFile.StoragePath);
                }
                catch (Exception e)
                {
                    logger.LogTrace("Couldn't delete the final path of the item failed to upload: {@E}", e);
                }

                try
                {
                    await remoteStorage.DeleteObject(version.StorageFile.UploadPath);
                }
                catch (Exception e)
                {
                    logger.LogInformation("Couldn't delete the upload path of the item: {@E}", e);
                }

                database.StorageFiles.Remove(version.StorageFile);
            }
            else
            {
                logger.LogWarning("Version to delete due to failed upload is missing the storage file");
            }

            database.StorageItemVersions.Remove(version);
        }

        // Not saved as we want to make sure the delete info is kept
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();
    }
}
