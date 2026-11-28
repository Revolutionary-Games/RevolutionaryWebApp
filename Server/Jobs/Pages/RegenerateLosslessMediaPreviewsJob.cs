namespace RevolutionaryWebApp.Server.Jobs.Pages;

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Models.Pages;
using Shared.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using Utilities;

public class RegenerateLosslessMediaPreviewsJob(ApplicationDbContext database, IMediaStorage mediaStorage,
    IHttpClientFactory clientFactory, IConfiguration configuration, ILogger<RegenerateLosslessMediaPreviewsJob> logger)
{
    public async Task Execute(long id, CancellationToken cancellationToken)
    {
        string pagePath;
        string thumbnailPath;
        await using (var transaction = await MediaFileLifecycle.BeginTransaction(database, cancellationToken))
        {
            var file = await MediaFileLifecycle.FindForUpdate(database, id, cancellationToken);

            // A soft-deleted file can still be restored. Only permanent purging prevents regeneration.
            if (file == null || file.PurgeStarted || !file.Processed || file.AllowLossyWebpPreviews ||
                !string.Equals(Path.GetExtension(file.Name), ".png", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var originalPath = MediaFileExtensions.GetStoragePath(file, MediaFileSize.Original);
            await using var original = await mediaStorage.GetObjectContent(originalPath);
            using var image = await Image.LoadAsync(original, cancellationToken);
            if (image.Metadata.DecodedImageFormat is not PngFormat)
                throw new InvalidImageContentException("The stored original is not a PNG");

            var page = MediaImagePreviews.CreatePageImage(image);
            using var resizedPage = ReferenceEquals(page, image) ? null : page;
            var thumbnail = MediaImagePreviews.CreateThumbnailImage(image);
            using var resizedThumbnail = ReferenceEquals(thumbnail, image) ? null : thumbnail;
            using var buffer = new MemoryStream();

            await MediaImagePreviews.UploadPngWebpPreviews(mediaStorage, file, page, thumbnail, buffer,
                cancellationToken);

            pagePath = MediaImagePreviews.GetPngWebpPagePath(file);
            thumbnailPath = MediaFileExtensions.GetStoragePath(file, MediaFileSize.Thumbnail);

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }

        // A failed purge propagates to Hangfire so it can retry after the replacements are available.
        await PurgeCachedPreviews(pagePath, thumbnailPath, cancellationToken);
        logger.LogInformation("Regenerated lossless WebP previews for media file {Id}", id);
    }

    private async Task PurgeCachedPreviews(string pagePath, string thumbnailPath, CancellationToken cancellationToken)
    {
        var apiKey = configuration["CDN:BunnyCDN:APIKey"];
        if (string.IsNullOrEmpty(apiKey) ||
            !Uri.TryCreate(configuration["MediaStorage:Download:URL"], UriKind.Absolute, out var baseUrl))
        {
            logger.LogWarning("CDN API key or media download URL missing, skipping media preview cache purge");
            return;
        }

        using var client = clientFactory.CreateClient("bunny");
        foreach (var path in new[] { pagePath, thumbnailPath })
        {
            var url = new Uri(baseUrl, path).AbsoluteUri;
            using var request = new HttpRequestMessage(HttpMethod.Post, "purge" + QueryString.Create("url", url));
            request.Headers.Add("AccessKey", apiKey);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }
}
