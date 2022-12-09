namespace ThriveDevCenter.Server.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Shared.Models;

public interface IBaseCDNDownload
{
    public bool Configured { get; }
    public string GenerateSignedURL(string path, TimeSpan expiresIn);
}

public abstract class BaseCDNDownload : IBaseCDNDownload
{
    private readonly string? downloadKey;

    protected BaseCDNDownload(string cdnBaseUrl, string downloadKey)
    {
        if (string.IsNullOrEmpty(cdnBaseUrl) || string.IsNullOrEmpty(downloadKey))
        {
            Configured = false;
        }
        else
        {
            this.downloadKey = downloadKey;
            CDNBaseUrl = new Uri(cdnBaseUrl);
            Configured = true;
        }
    }

    public Uri? CDNBaseUrl { get; }

    public bool Configured { get; }

    public string GenerateSignedURL(string path, TimeSpan expiresIn)
    {
        ThrowIfNotConfigured();

        if (path[0] != '/')
            path = '/' + path;

        // CDN token needs to be made from the unescaped path
        var unescaped = path;

        // But special characters in URL paths need to be escaped for the URL sent to the client for S3 storage
        // to work
        path = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

        long expirationTimestamp = (DateTimeOffset.UtcNow + expiresIn).ToUnixTimeSeconds();

        var fullUri = new Uri(CDNBaseUrl!, path);

        var unhashedKey = $"{downloadKey}{unescaped}{expirationTimestamp}";

        // IP validation could be used here if not for ipv6 and ipv4 mixed use
        // Now it's possible to use countries, so that is likely much more reliable regarding ipv4 and ipv6
        // addresses for the same computer

        return QueryHelpers.AddQueryString(fullUri.ToString(), new Dictionary<string, string?>
        {
            { "token", HashToken(unhashedKey) },
            { "expires", expirationTimestamp.ToString() },
        });
    }

    protected void ThrowIfNotConfigured()
    {
        if (Configured && CDNBaseUrl != null && downloadKey != null)
            return;

        string error = "The server storage is not configured properly";

        throw new HttpResponseException
        {
            Status = StatusCodes.Status500InternalServerError,
            Value = new BasicJSONErrorResult(error, error).ToString(),
        };
    }

    /// <summary>
    ///   Creates a hashed, ready to use version of the unhashed key for BunnyCDN
    /// </summary>
    private static string HashToken(string unhashedKey)
    {
        var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(unhashedKey));
        return ReplaceChars(Convert.ToBase64String(hash));
    }

    /// <summary>
    ///   BunnyCDN requires an additional character replacement operation on top of base64 encoding
    /// </summary>
    private static string ReplaceChars(string base64String)
    {
        return base64String.Replace("\n", string.Empty).Replace("+", "-").Replace("/", "_").Replace("=", string.Empty);
    }
}
