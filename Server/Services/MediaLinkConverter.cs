namespace RevolutionaryWebApp.Server.Services;

using System;
using Microsoft.Extensions.Configuration;
using Shared.Models.Pages;
using Shared.Services;
using Shared.Utilities;

public class MediaLinkConverter : IMediaLinkConverter
{
    private readonly string baseUrl;

    public MediaLinkConverter(IConfiguration configuration)
    {
        baseUrl = configuration["MediaStorage:Download:URL"] ?? string.Empty;

        if (string.IsNullOrEmpty(baseUrl))
            throw new Exception("Media download URL is not configured");

        if (baseUrl[^1] != '/')
            throw new Exception("Media url should end with a slash");
    }

    public string TranslateImageLink(string imageType, string globalId, MediaFileSize size)
    {
        if (size == MediaFileSize.Original)
            throw new ArgumentException("This method doesn't support getting original size");

        return baseUrl + MediaFileExtensions.GetViewPath(globalId, size, imageType);
    }

    public string GetGeneratedAndProxyImagePrefix()
    {
        // These server-rendered pages are shown served already from the CDN, which has an implicit "/live" root,
        // so we return just an empty prefix here
        return string.Empty;
    }
}
