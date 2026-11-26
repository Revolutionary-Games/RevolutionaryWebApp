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
using SixLabors.ImageSharp.Processing.Processors.Quantization;
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
            encoder = new GifEncoder
            {
                ColorTableMode = FrameColorTableMode.Global,
                Quantizer = new WuQuantizer(new QuantizerOptions
                {
                    Dither = null,
                }),
            };
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
                var webpMetadata = image.Metadata.GetWebpMetadata();
                if (webpMetadata != null)
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
            var large = CreateLargeImage(image, reSampler);
            using var resizedLarge = ReferenceEquals(large, image) ? null : large;

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
            var page = MediaImagePreviews.CreatePageImage(image);
            using var resizedPage = ReferenceEquals(page, image) ? null : page;

            await SaveAndUploadImage(page, encoder, pagePath, mimeType, imageDataStream, cancellationToken);

            // Thumbnail
            using var firstFrameOnly = CreateStaticPreviewSource(image);
            var thumbnailSource = firstFrameOnly ?? image;
            var thumbnail = MediaImagePreviews.CreateThumbnailImage(thumbnailSource);
            using var resizedThumbnail = ReferenceEquals(thumbnail, thumbnailSource) ? null : thumbnail;

            if (imageType == ".png")
            {
                await MediaImagePreviews.UploadPngWebpPreviews(mediaStorage, mediaFile, page, thumbnail,
                    imageDataStream, cancellationToken);
            }
            else if (AppInfo.MediaPreviewFileExtension == ".webp")
            {
                var previewEncoder = new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = AppInfo.PreviewImageQuality,
                };
                await SaveAndUploadImage(thumbnail, previewEncoder, thumbPath, "image/webp", imageDataStream,
                    cancellationToken);
            }

            // And finally upload the original with the metadata stripped
            await SaveAndUploadImage(image, encoder, originalPath, mimeType, imageDataStream, cancellationToken);
            mediaFile.OriginalWidth = image.Width;
            mediaFile.OriginalHeight = image.Height;
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
            mediaFile.OriginalFileSize = imageDataStream.Length;
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

    internal static Image? CreateStaticPreviewSource(Image image)
    {
        if (!AppInfo.RemovePreviewAnimations || image.Frames.Count <= 1)
            return null;

        // Need to have a clone with empty lambda here to not modify the original
        var copy = image.Clone(i => _ = 0);
        while (copy.Frames.Count > 1)
            copy.Frames.RemoveFrame(copy.Frames.Count - 1);

        return copy;
    }

    private static Image CreateLargeImage(Image image, IResampler reSampler)
    {
        return ImageHelpers.DownSampleImageIfLargerThan(image, AppInfo.MediaResolutionLarge,
            AppInfo.MediaResolutionLarge, reSampler);
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
