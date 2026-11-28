namespace RevolutionaryWebApp.Server.Jobs.Pages;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Utilities;

/// <summary>
///   Used to delete failed / not completed uploads to <see cref="IUploadFileStorage"/>
/// </summary>
public class DeleteUploadStorageFileJob
{
    private readonly ILogger<DeleteUploadStorageFileJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IUploadFileStorage uploadFileStorage;

    public DeleteUploadStorageFileJob(ILogger<DeleteUploadStorageFileJob> logger, NotificationsEnabledDb database,
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
        await using var transaction = otherThingToDelete == RelatedRecordType.MediaFile ?
            await MediaFileLifecycle.BeginTransaction(database, cancellationToken) : null;

        var mediaFile = otherThingToDelete switch
        {
            RelatedRecordType.None => null,
            RelatedRecordType.MediaFile => await MediaFileLifecycle.FindForUpdate(database, otherId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(otherThingToDelete), otherThingToDelete, null),
        };

        if (await uploadFileStorage.DoesObjectExist(path, cancellationToken))
        {
            logger.LogInformation("Deleting upload from remote storage content: {Path}", path);
            await uploadFileStorage.DeleteObject(path);
        }

        if (mediaFile == null || mediaFile.Processed || mediaFile.Deleted)
            return;

        mediaFile.MarkDeleted();
        await database.LogEntries.AddAsync(new LogEntry($"Marked failed media upload {otherId} as deleted")
        {
            Extended = path,
        }, cancellationToken);
        await database.SaveChangesAsync(cancellationToken);

        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);
    }
}
