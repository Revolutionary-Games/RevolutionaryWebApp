namespace RevolutionaryWebApp.Client.Utilities;

using System;
using System.Linq;
using Microsoft.AspNetCore.WebUtilities;

public static class YoutubeUrlParser
{
    public static bool TryGetVideoId(string? input, out string videoId)
    {
        videoId = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var url = input.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
            url = "https://" + url;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        string? id = null;

        if (host is "youtu.be" or "www.youtu.be")
        {
            if (path.Length == 1)
                id = path[0];
        }
        else if (host is "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" or
                 "youtube-nocookie.com" or "www.youtube-nocookie.com")
        {
            if (path.Length == 1 && path[0] == "watch")
            {
                var query = QueryHelpers.ParseQuery(uri.Query);
                if (query.TryGetValue("v", out var value) && value.Count == 1)
                    id = value[0];
            }
            else if (path.Length == 2 && path[0] is "embed" or "shorts" or "live")
            {
                id = path[1];
            }
        }

        if (id == null || id.Length != 11 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return false;

        videoId = id;
        return true;
    }
}
