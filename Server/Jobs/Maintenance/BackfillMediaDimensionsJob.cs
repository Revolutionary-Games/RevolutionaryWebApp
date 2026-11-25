namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Models.Pages;
using SixLabors.ImageSharp;

public class BackfillMediaDimensionsJob : MaintenanceJobBase
{
    private const int BatchSize = 100;

    private readonly IMediaStorage mediaStorage;

    public BackfillMediaDimensionsJob(ILogger<BackfillMediaDimensionsJob> logger, ApplicationDbContext operationDb,
        NotificationsEnabledDb operationStatusDb, IMediaStorage mediaStorage)
        : base(logger, operationDb, operationStatusDb)
    {
        this.mediaStorage = mediaStorage;
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        if (!mediaStorage.Configured)
            throw new InvalidOperationException("Media storage is not configured");

        long lastId = 0;
        int updated = 0;
        int failed = 0;

        while (true)
        {
            var batchCursor = lastId;
            var batch = await Database.MediaFiles
                .Where(file => file.Id > batchCursor && file.Processed && !file.Deleted &&
                    (file.OriginalWidth == 0 || file.OriginalHeight == 0))
                .OrderBy(file => file.Id).Take(BatchSize).ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var file in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await using var original = await mediaStorage.GetObjectContent(
                        file.GetStoragePath(MediaFileSize.Original)!);
                    var info = await Image.IdentifyAsync(original, cancellationToken);
                    file.OriginalWidth = info.Width;
                    file.OriginalHeight = info.Height;
                    ++updated;
                }
                catch (Exception e) when (e is AmazonS3Exception or IOException or UnknownImageFormatException or
                                              InvalidImageContentException or NotSupportedException)
                {
                    Logger.LogWarning(e, "Could not read original dimensions for media file {MediaFileId}", file.Id);
                    ++failed;
                }
            }

            await Database.SaveChangesAsync(cancellationToken);
            lastId = batch[^1].Id;

            foreach (var file in batch)
                Database.Entry(file).State = EntityState.Detached;
        }

        operationData.Failed = failed > 0;
        operationData.ExtendedDescription = $"Updated dimensions for {updated} media file(s). Failed: {failed}.";
    }
}
