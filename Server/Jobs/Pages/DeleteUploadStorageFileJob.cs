namespace RevolutionaryWebApp.Server.Jobs.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Services;

/// <summary>
///   Used to delete failed / not completed uploads to <see cref="IUploadFileStorage"/>
/// </summary>
public class DeleteUploadStorageFileJob
{
    private readonly ILogger<DeleteUploadStorageFileJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IUploadFileStorage uploadFileStorage;

    public DeleteUploadStorageFileJob(ILogger<DeleteUploadStorageFileJob> logger, ApplicationDbContext database,
        IUploadFileStorage uploadFileStorage)
    {
        this.logger = logger;
        this.database = database;
        this.uploadFileStorage = uploadFileStorage;
    }

    public enum RelatedRecordType
    {
        None,
        MediaFile,
    }

    public async Task Execute(string path, RelatedRecordType otherThingToDelete, long otherId,
        CancellationToken cancellationToken)
    {
        var exists = await uploadFileStorage.DoesObjectExist(path, cancellationToken);

        MediaFile? mediaFile = null;

        if (otherThingToDelete == RelatedRecordType.MediaFile)
        {
            mediaFile = await database.MediaFiles.FindAsync(new object[] { otherId }, cancellationToken);

            if (mediaFile == null)
                logger.LogError("Cannot find related MediaFile in upload success check");
        }

        if (!exists)
        {
            logger.LogDebug("Upload storage doesn't have file that would be expired: {Path}", path);
        }
        else
        {
            logger.LogInformation("Deleting failed upload storage item: {Path}", path);
            await uploadFileStorage.DeleteObject(path);
        }

        switch (otherThingToDelete)
        {
            case RelatedRecordType.None:
                // No DB changes to save
                return;
            case RelatedRecordType.MediaFile:
                if (mediaFile == null)
                    return;

                database.MediaFiles.Remove(mediaFile);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(otherThingToDelete), otherThingToDelete, null);
        }

        // ReSharper disable once MethodSupportsCancellation
        await database.LogEntries.AddAsync(
            new LogEntry($"Deleted failed user upload and associated data with id {otherId}")
            {
                Extended = path,
            });

        // This is not cancelled as the data is already gone
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();
    }
}
