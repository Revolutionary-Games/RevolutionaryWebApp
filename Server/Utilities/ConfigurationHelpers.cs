namespace RevolutionaryWebApp.Server.Utilities;

using System;
using Microsoft.Extensions.Configuration;

public static class ConfigurationHelpers
{
    public static Uri GetBaseUrl(this IConfiguration configuration)
    {
        return new Uri(configuration["BaseUrl"] ?? throw new InvalidOperationException("Base url is missing"));
    }

    /// <summary>
    ///   CDN base of the live website view. This should have a trailing slash when converted to a string.
    /// </summary>
    /// <param name="configuration">Config to read from</param>
    /// <returns>Configured CDN or null if not configured</returns>
    public static Uri? GetLiveWWWBaseUrl(this IConfiguration configuration)
    {
        var live = configuration["CDN:LiveUrl"];

        if (string.IsNullOrWhiteSpace(live))
            return null;

        return new Uri(live);
    }

    public static string GetCDNPrefixWWW(this IConfiguration configuration)
    {
        var value = configuration["CDN:ContentBase"];

        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (value.EndsWith('/'))
            throw new Exception("Content CDN base shouldn't end with a slash");

        return value;
    }

    /// <summary>
    ///   Gets the base URL that has all the WWW site assets underneath it. If CDN is configured returns what
    ///   <see cref="GetCDNPrefixWWW"/> returns.
    /// </summary>
    /// <returns>Base URL for WWW assets</returns>
    public static Uri GetWWWAssetBaseUrl(this IConfiguration configuration)
    {
        var cdn = GetCDNPrefixWWW(configuration);

        if (!string.IsNullOrEmpty(cdn))
            return new Uri(cdn);

        // When not using a CDN assets are relative to the main base URL
        return GetBaseUrl(configuration);
    }

    /// <summary>
    ///   Returns a base url relative full url. TODO: if relative part is full url it should take precedence
    /// </summary>
    /// <param name="configuration">The configuration to read from</param>
    /// <param name="key">The configuration key to read the relative part from</param>
    /// <returns>The full url</returns>
    public static Uri BaseUrlRelative(this IConfiguration configuration, string key)
    {
        return new Uri(configuration.GetBaseUrl(), configuration[key]);
    }
}
