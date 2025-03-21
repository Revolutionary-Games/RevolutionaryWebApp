namespace RevolutionaryWebApp.Server.Services;

using System;
using Microsoft.Extensions.Configuration;
using Shared.Models.Pages;
using Shared.Services;
using Shared.Utilities;

public class MediaLinkConverter : IMediaLinkConverter
{
    private readonly string baseUrl;

    private readonly string proxyImageBase;

    public MediaLinkConverter(IConfiguration configuration)
    {
        baseUrl = configuration["MediaStorage:Download:URL"] ?? string.Empty;

        if (string.IsNullOrEmpty(baseUrl))
            throw new Exception("Media download URL is not configured");

        if (baseUrl[^1] != '/')
            throw new Exception("Media url should end with a slash");

        // Server rendered pages can be shown through the CDN (published) or as previews, so for max compatibility
        // we always use the CDN URL as a prefix
        proxyImageBase = configuration["CDN:LiveUrl"] ?? string.Empty;

        // Except when it is not configured, we assume we are running locally without a CDN
        if (string.IsNullOrWhiteSpace(proxyImageBase))
            proxyImageBase = "/live";
    }

    public string TranslateImageLink(string imageType, string globalId, MediaFileSize size)
    {
        if (size == MediaFileSize.Original)
            throw new ArgumentException("This method doesn't support getting original size");

        return baseUrl + MediaFileExtensions.GetViewPath(globalId, size, imageType);
    }

    public string GetGeneratedAndProxyImagePrefix()
    {
        return proxyImageBase;
    }
}
