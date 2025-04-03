namespace RevolutionaryWebApp.Server.Jobs.Pages;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Hangfire;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Services;
using Shared;
using Shared.Models.Pages;
using Shared.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;
using Utilities;

/// <summary>
///   Processes an image to generate all the required sizes
/// </summary>
public class ProcessUploadedImageJob
{
    private readonly ILogger<ProcessUploadedImageJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IUploadFileStorage uploadFileStorage;
    private readonly IMediaStorage mediaStorage;
    private readonly IBackgroundJobClient jobClient;

    public ProcessUploadedImageJob(ILogger<ProcessUploadedImageJob> logger, NotificationsEnabledDb database,
        IUploadFileStorage uploadFileStorage, IMediaStorage mediaStorage, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.uploadFileStorage = uploadFileStorage;
        this.mediaStorage = mediaStorage;
        this.jobClient = jobClient;
    }

    public async Task Execute(long itemId, string processPath, CancellationToken cancellationToken)
    {
        var mediaFile = await database.MediaFiles.FindAsync(new object[] { itemId }, cancellationToken);

        if (mediaFile == null)
        {
            logger.LogError("Media file to be processed doesn't exist, will try to clear storage at least");

            try
            {
                await uploadFileStorage.DeleteObject(processPath);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while trying to clear storage of failed upload");
            }

            return;
        }

        ImageEncoder previewEncoder;

        // Guard against changing this and forgetting to update this code
        if (AppInfo.MediaPreviewFileExtension == ".webp")
        {
            previewEncoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = AppInfo.PreviewImageQuality,
            };
        }

        var imageType = Path.GetExtension(mediaFile.Name);

        ImageEncoder encoder;
        string mimeType;
        if (imageType == ".png")
        {
            encoder = new PngEncoder
            {
                CompressionLevel = PngCompressionLevel.BestCompression,
            };

            mimeType = "image/png";
        }
        else if (imageType is ".jpg" or ".jpeg")
        {
            encoder = new JpegEncoder
            {
                Quality = 75,
            };

            mimeType = "image/jpeg";
        }
        else if (imageType == ".gif")
        {
            encoder = new GifEncoder();
            mimeType = "image/gif";
        }
        else if (imageType == ".webp")
        {
            encoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossy,
                Quality = 85,
                Method = WebpEncodingMethod.BestQuality,
            };
            mimeType = "image/webp";
        }
        else
        {
            throw new Exception($"Unhandled image type: {imageType}");
        }

        if (mediaFile.GetIntermediateProcessingPath() != processPath)
            logger.LogError("Media file processing path doesn't match what is provided as a job parameter");

        // Lanczos is used for sharper preview images
        var reSampler = KnownResamplers.Lanczos3;

        logger.LogInformation("Processing all image sizes from: {ProcessPath}", processPath);
        bool success = true;

        var originalPath = MediaFileExtensions.GetStoragePath(mediaFile, MediaFileSize.Original);
        var largePath = MediaFileExtensions.GetStoragePath(mediaFile, MediaFileSize.Large);
        var pagePath = MediaFileExtensions.GetStoragePath(mediaFile, MediaFileSize.FitPage);
        var thumbPath = MediaFileExtensions.GetStoragePath(mediaFile, MediaFileSize.Thumbnail);

        using var imageDataStream = new MemoryStream();

        try
        {
            await using var readStream = await uploadFileStorage.GetObjectContent(processPath);

            using var image = await Image.LoadAsync(readStream, cancellationToken);

            if (imageType == ".webp")
            {
                if (image.Metadata.TryGetWebpMetadata(out WebpMetadata? webpMetadata))
                {
                    // Check if it was compressed with a lossless format
                    if (webpMetadata.FileFormat == WebpFileFormatType.Lossless)
                    {
                        // Re-create the encoder to keep the lossless format
                        encoder = new WebpEncoder
                        {
                            FileFormat = WebpFileFormatType.Lossless,
                            Method = WebpEncodingMethod.BestQuality,
                        };
                    }
                    else if (webpMetadata.FileFormat == WebpFileFormatType.Lossy)
                    {
                    }
                    else
                    {
                        logger.LogWarning("Unrecognized WebP file format");
                    }
                }
                else
                {
                    logger.LogWarning("Could not retrieve WebP metadata");
                }
            }

            // TODO: orientation data might be something to keep:
            // https://github.com/SixLabors/ImageSharp.Web/discussions/226

            // Scrub any potentially unsafe metadata
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;

            // Large
            using var large = CreateLargeImage(image, reSampler);

            // Upload is waited for here, which might hopefully make sure this works correctly
            await SaveAndUploadImage(large, encoder, largePath, mimeType, imageDataStream, cancellationToken);

            // Make the smaller files slightly higher in quality if they are losslessly compressed
            if (encoder is JpegEncoder)
            {
                encoder = new JpegEncoder
                {
                    Quality = 85,
                };
            }
            else if (encoder is WebpEncoder { FileFormat: WebpFileFormatType.Lossy })
            {
                encoder = new WebpEncoder
                {
                    Quality = 90,
                    Method = WebpEncodingMethod.BestQuality,
                };
            }

            // Page
            using var page = CreatePageImage(image, reSampler);

            await SaveAndUploadImage(page, encoder, pagePath, mimeType, imageDataStream, cancellationToken);

            // Thumbnail
            using var thumbnail = CreateThumbnailImage(image, reSampler);

            await SaveAndUploadImage(thumbnail, previewEncoder, thumbPath, mimeType, imageDataStream,
                cancellationToken);

            // TODO: should lossless images have jpg variants generated for them for preview purposes?

            // And finally upload the original with the metadata stripped
            await SaveAndUploadImage(image, encoder, originalPath, mimeType, imageDataStream, cancellationToken);
            logger.LogInformation("Successfully generated other resolutions for {Name}", mediaFile.Name);
        }
        catch (NotSupportedException e)
        {
            logger.LogError(e, "Error with image format in media file processing");
            success = false;
        }
        catch (InvalidImageContentException e)
        {
            logger.LogError(e, "Error with image content in media file processing");
            success = false;
        }
        catch (UnknownImageFormatException e)
        {
            logger.LogError(e, "Error with image format detection in media file processing");
            success = false;
        }
        catch (Exception e)
        {
            logger.LogError(e,
                "Error while processing uploaded media file, hopefully this is an error that goes away with retries");
            throw;
        }

        if (success)
        {
            mediaFile.Processed = true;
            mediaFile.BumpUpdatedAt();
            logger.LogInformation("Processed media file: {Id}", mediaFile.Id);

            // Once done with the original data, delete it. This is done through a job to ensure this gets done even if
            // there are connection errors.
            jobClient.Schedule<DeleteUploadStorageFileJob>(
                x => x.Execute(processPath, DeleteUploadStorageFileJob.RelatedRecordType.None, -1, cancellationToken),
                TimeSpan.FromSeconds(30));
        }
        else
        {
            logger.LogError("Error media file processing failed for: {Id}", mediaFile.Id);
            await OnError(mediaFile);
        }

        // This is not cancelled as the data is already handled
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();
    }

    private static Image CreateLargeImage(Image image, IResampler reSampler)
    {
        return ImageHelpers.DownSampleImageIfLargerThan(image, AppInfo.MediaResolutionLarge,
            AppInfo.MediaResolutionLarge, reSampler);
    }

    private static Image CreatePageImage(Image image, IResampler reSampler)
    {
        return ImageHelpers.DownSampleImageIfLargerThan(image, AppInfo.MediaResolutionPage, AppInfo.MediaResolutionPage,
            reSampler);
    }

    private static Image CreateThumbnailImage(Image image, IResampler reSampler)
    {
        if (AppInfo.RemovePreviewAnimations && image.Frames.Count > 1)
        {
            // Need to have a clone with empty lambda here to not modify the original
            image = image.Clone(i => _ = 0);

            while (image.Frames.Count > 1)
                image.Frames.RemoveFrame(image.Frames.Count - 1);
        }

        return ImageHelpers.DownSampleImageIfLargerThan(image, AppInfo.MediaResolutionThumbnail,
            AppInfo.MediaResolutionThumbnail, reSampler);
    }

    private async Task SaveAndUploadImage(Image image, ImageEncoder encoder, string targetPath, string mime,
        MemoryStream imageDataStream, CancellationToken cancellationToken)
    {
        await ImageHelpers.SaveImageToMemoryStream(image, imageDataStream, encoder, cancellationToken);

        // Then upload
        try
        {
            // We reuse the stream, so we do not want it to be closed
            await mediaStorage.UploadFile(targetPath, imageDataStream, mime, false, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while uploading resized image");
            throw;
        }
    }

    private async Task OnError(MediaFile mediaFile)
    {
        // TODO: should somehow send an error notification to the person who tried to upload the file to notify of the
        // problem. Could theoretically send an email here but that could spam quite a lot.

        await database.LogEntries.AddAsync(new LogEntry($"Failed to process uploaded media file {mediaFile.Id}")
        {
            Extended = mediaFile.Name,
        });
    }
}
