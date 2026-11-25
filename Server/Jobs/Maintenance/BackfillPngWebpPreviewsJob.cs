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
using Shared.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Utilities;

public class BackfillPngWebpPreviewsJob : MaintenanceJobBase
{
    private const int BatchSize = 100;

    private readonly IMediaStorage mediaStorage;

    public BackfillPngWebpPreviewsJob(ILogger<BackfillPngWebpPreviewsJob> logger, ApplicationDbContext operationDb,
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
        using var buffer = new MemoryStream();

        while (true)
        {
            var batchCursor = lastId;
            var batch = await Database.MediaFiles.AsNoTracking()
                .Where(file => file.Id > batchCursor && file.Processed && !file.Deleted &&
                    file.Name.ToLower().EndsWith(".png"))
                .OrderBy(file => file.Id).Take(BatchSize).ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var file in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var pagePath = MediaImagePreviews.GetPngWebpPagePath(file);
                    var thumbnailPath = MediaFileExtensions.GetStoragePath(file, MediaFileSize.Thumbnail);
                    if (await mediaStorage.DoesObjectExist(pagePath, cancellationToken) &&
                        await mediaStorage.DoesObjectExist(thumbnailPath, cancellationToken))
                    {
                        continue;
                    }

                    await using var original = await mediaStorage.GetObjectContent(
                        file.GetStoragePath(MediaFileSize.Original)!);
                    using var image = await Image.LoadAsync(original, cancellationToken);

                    if (image.Metadata.DecodedImageFormat is not PngFormat)
                        throw new InvalidImageContentException("The stored original is not a PNG");

                    var page = MediaImagePreviews.CreatePageImage(image);
                    using var resizedPage = ReferenceEquals(page, image) ? null : page;
                    var thumbnail = MediaImagePreviews.CreateThumbnailImage(image);
                    using var resizedThumbnail = ReferenceEquals(thumbnail, image) ? null : thumbnail;

                    await MediaImagePreviews.UploadPngWebpPreviews(mediaStorage, file, page, thumbnail, buffer,
                        cancellationToken);

                    ++updated;
                }
                catch (Exception e) when (e is AmazonS3Exception or IOException or UnknownImageFormatException or
                                              InvalidImageContentException or NotSupportedException)
                {
                    Logger.LogWarning(e, "Could not generate WebP previews for media file {MediaFileId}", file.Id);
                    ++failed;
                }
            }

            lastId = batch[^1].Id;
        }

        operationData.Failed = failed > 0;
        operationData.ExtendedDescription = $"Generated WebP previews for {updated} PNG file(s). Failed: {failed}.";
    }
}
