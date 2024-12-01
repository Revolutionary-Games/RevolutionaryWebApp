namespace RevolutionaryWebApp.Server.Services;

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Shared.Models;

public class SiteConfigForClientGenerator
{
    private readonly string mediaBaseUrl;

    private string? cached;

    public SiteConfigForClientGenerator(IConfiguration configuration)
    {
        mediaBaseUrl = configuration["MediaStorage:Download:URL"] ?? string.Empty;

        if (string.IsNullOrEmpty(mediaBaseUrl))
            mediaBaseUrl = "/";
    }

    public SiteConfigData Generate()
    {
        return new SiteConfigData(mediaBaseUrl);
    }

    public string GenerateString()
    {
        cached ??= JsonSerializer.Serialize(Generate());

        return cached;
    }
}
