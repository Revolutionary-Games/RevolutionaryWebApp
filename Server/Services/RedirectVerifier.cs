namespace ThriveDevCenter.Server.Services;

using System;
using Microsoft.Extensions.Configuration;
using Utilities;

/// <summary>
///   Checks redirects that they are safe before sending them to the client
/// </summary>
public class RedirectVerifier
{
    private readonly string baseUrl;

    public RedirectVerifier(IConfiguration configuration)
    {
        baseUrl = configuration["BaseUrl"] ?? string.Empty;

        if (string.IsNullOrEmpty(baseUrl))
            throw new Exception("Base URL is not configured");

        if (baseUrl[^1] != '/')
            throw new Exception("Base url should end with a slash");

        // Make sure that accessing this doesn't cause an exception
        configuration.GetBaseUrl();
    }

    /// <summary>
    ///   Sanitizes a potentially tampered with redirect url
    /// </summary>
    /// <param name="url">The url to process</param>
    /// <param name="sanitized">The sanitized url (or null if can't sanitize)</param>
    /// <returns>True when sanitization was possible and the result is a safe redirect</returns>
    public bool SanitizeRedirectUrl(string url, out string? sanitized)
    {
        // For now just require the base url part to match
        if (url.StartsWith(baseUrl))
        {
            sanitized = url;
            return true;
        }

        sanitized = null;
        return false;
    }
}
