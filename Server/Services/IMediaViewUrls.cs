namespace RevolutionaryWebApp.Server.Services;

using System;
using Microsoft.Extensions.Configuration;
using Models.Pages;
using Shared.Models.Pages;

public interface IMediaViewUrls
{
    /// <summary>
    ///   View URL for a file. Returns null if file is not good for viewing (deleted or something like that)
    /// </summary>
    /// <param name="file">The file to generate the URL for</param>
    /// <param name="size">The preferred size of the image, smaller sizes are at different URLs</param>
    /// <returns>URL to view the media file, or null if the file is not good</returns>
    public string? CreateViewUrlFor(MediaFile file, MediaFileSize size);
}

public class MediaViewUrls : IMediaViewUrls
{
    private readonly bool configured;
    private readonly Uri baseUrl;

    public MediaViewUrls(IConfiguration configuration)
    {
        var url = configuration["MediaStorage:Download:URL"];

        if (string.IsNullOrEmpty(url))
        {
            configured = false;
            baseUrl = null!;
            return;
        }

        baseUrl = new Uri(url);

        configured = true;
    }

    public string? CreateViewUrlFor(MediaFile file, MediaFileSize size)
    {
        if (!configured)
            throw new InvalidOperationException("Media storage download URL not configured");

        var storage = file.GetStoragePath(size);

        if (storage == null)
            return null;

        return new Uri(baseUrl, storage).ToString();
    }
}
