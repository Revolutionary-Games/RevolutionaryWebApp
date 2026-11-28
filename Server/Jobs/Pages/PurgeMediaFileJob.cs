namespace RevolutionaryWebApp.Server.Jobs.Pages;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Models.Pages;
using Shared.Utilities;
using Utilities;

public class PurgeMediaFileJob
{
    private readonly NotificationsEnabledDb database;
    private readonly IMediaStorage mediaStorage;
    private readonly IUploadFileStorage uploadStorage;
    private readonly ILogger<PurgeMediaFileJob> logger;

    public PurgeMediaFileJob(NotificationsEnabledDb database, IMediaStorage mediaStorage,
        IUploadFileStorage uploadStorage, ILogger<PurgeMediaFileJob> logger)
    {
        this.database = database;
        this.mediaStorage = mediaStorage;
        this.uploadStorage = uploadStorage;
        this.logger = logger;
    }

    public async Task Execute(long id, DateTime deletedAt, bool immediate, CancellationToken cancellationToken)
    {
        // Persist the transition before touching remote storage. A partial failure must leave the file
        // eligible for retry and prevent restoring a file with missing variants.
        await using (var start = await MediaFileLifecycle.BeginTransaction(database, cancellationToken))
        {
            var file = await MediaFileLifecycle.FindForUpdate(database, id, cancellationToken);
            if (file == null || !file.Deleted || (file.DeletedAt ?? file.UpdatedAt) != deletedAt)
                return;

            if (!file.PurgeStarted)
            {
                var retention = file.Processed ?
                    MediaFileLifecycle.ProcessedRetentionDays : MediaFileLifecycle.FailedRetentionDays;
                if (!immediate && deletedAt > DateTime.UtcNow.AddDays(-retention))
                    return;

                var usage = await MediaFileLifecycle.FindUsage(database, file, cancellationToken);
                if (usage != null)
                {
                    logger.LogWarning("Cannot purge media file {Id} because it is still used by {Usage}", id, usage);
                    return;
                }

                if (!mediaStorage.Configured || !uploadStorage.Configured)
                    throw new InvalidOperationException("Media storage or upload storage is not configured");

                file.PurgeStarted = true;
                await database.SaveChangesAsync(cancellationToken);
            }

            if (start != null)
                await start.CommitAsync(cancellationToken);
        }

        // Hold the lock while deleting so repeated jobs for this file cannot race with one another.
        await using var transaction = await MediaFileLifecycle.BeginTransaction(database, cancellationToken);
        var mediaFile = await MediaFileLifecycle.FindForUpdate(database, id, cancellationToken);
        if (mediaFile == null || !mediaFile.Deleted || !mediaFile.PurgeStarted)
            return;

        var paths = Enum.GetValues<MediaFileSize>().Select(size => MediaFileExtensions.GetStoragePath(mediaFile, size))
            .ToHashSet(StringComparer.Ordinal);

        var extension = Path.GetExtension(mediaFile.Name);

        // PNG automatic webp preview handling
        if (string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase))
            paths.Add(MediaImagePreviews.GetPngWebpPagePath(mediaFile));

        // Older uploads used thumbnails in the original format and filename-based resize suffixes.
        paths.Add($"{mediaFile.GlobalId}/thumb{extension}");
        var legacyBase = $"{mediaFile.GlobalId}/{Path.GetFileNameWithoutExtension(mediaFile.Name)}";
        paths.Add($"{legacyBase}_large{extension}");
        paths.Add($"{legacyBase}_fit{extension}");
        paths.Add($"{legacyBase}_thumb{extension}");

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await mediaStorage.DeleteObject(path);
        }

        await uploadStorage.DeleteObject(mediaFile.GetIntermediateProcessingPath());
        await uploadStorage.DeleteObject(mediaFile.GetUploadPath());

        // The usage index can still contain old references, including those from deleted pages.
        await database.MediaFileUsages.Where(usage => usage.MediaFileGuid == mediaFile.GlobalId)
            .ExecuteDeleteAsync(cancellationToken);
        database.MediaFiles.Remove(mediaFile);
        await database.SaveChangesAsync(cancellationToken);

        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Permanently purged media file {Id}", id);
    }
}
