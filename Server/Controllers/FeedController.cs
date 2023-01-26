namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using AngleSharp.Io;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using Models;
using Services;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class FeedController : Controller
{
    private readonly IConfiguration configuration;
    private readonly ApplicationDbContext database;
    private readonly CustomMemoryCache cache;

    public FeedController(IConfiguration configuration, ApplicationDbContext database, CustomMemoryCache cache)
    {
        this.configuration = configuration;
        this.database = database;
        this.cache = cache;
    }

    // Redirect some common things feed's readers might attempt to access
    [HttpGet]
    [ResponseCache(Duration = 500)]
    public IActionResult Get()
    {
        return Redirect(configuration.GetBaseUrl().ToString());
    }

    [HttpGet("favicon.png")]
    [HttpGet("favicon.ico")]
    [ResponseCache(Duration = 500)]
    public IActionResult GetFavicon()
    {
        return Redirect(new Uri(configuration.GetBaseUrl(), "/favicon.png").ToString());
    }

    [HttpGet("{name}")]
    public async Task<ActionResult<string>> GetFeed([Required] string name)
    {
        if (name.Length > 200)
            return BadRequest("Too long name in request");

        var headers = HttpContext.Response.GetTypedHeaders();

        var cacheKey = $"feed/{name}";

        // As we have dynamic expire times, we can't use the normal response caching here
        if (cache.Cache.TryGetValue(cacheKey, out object? rawCacheEntry) && rawCacheEntry is CacheEntry cacheEntry)
        {
            headers.CacheControl = new CacheControlHeaderValue
            {
                MaxAge = cacheEntry.ClientCacheTime,
                Public = true,
            };

            if (!cacheEntry.Success || cacheEntry.Content == null)
                return NotFound("No such feed");

            headers.ContentType = cacheEntry.ContentType;

            headers.Date = cacheEntry.ContentTime;

            return cacheEntry.Content;
        }

        TimeSpan? cacheTime = null;
        string? content = null;

        // First try to find a feed
        var feed = await database.Feeds.AsNoTracking().FirstOrDefaultAsync(f =>
            !f.Deleted && (f.Name == name ||
                (f.HtmlFeedVersionSuffix != null && f.Name + f.HtmlFeedVersionSuffix == name)));

        if (feed != null)
        {
            if (name == feed.Name)
            {
                content = feed.LatestContent;
            }
            else if (!string.IsNullOrEmpty(feed.HtmlFeedVersionSuffix))
            {
                // Html content wanted
                content = feed.HtmlLatestContent;
                headers.ContentType = MediaTypeHeaderValue.Parse(MimeTypeNames.Html);
            }

            if (content != null)
            {
                cacheTime = feed.CacheTime ?? feed.PollInterval;

                if (feed.ContentUpdatedAt != null)
                    headers.Date = feed.ContentUpdatedAt.Value;
            }
        }
        else
        {
            // Try to find a combined feed
            var combined = await database.CombinedFeeds.AsNoTracking().FirstOrDefaultAsync(c => c.Name == name);

            if (combined != null)
            {
                content = combined.LatestContent;

                if (content != null)
                {
                    cacheTime = combined.CacheTime;
                    headers.ContentType = MediaTypeHeaderValue.Parse(MimeTypeNames.Html);

                    if (combined.ContentUpdatedAt != null)
                        headers.Date = combined.ContentUpdatedAt.Value;
                }
            }
        }

        // Default cache when we don't find anything
        cacheTime ??= TimeSpan.FromSeconds(60);

        headers.CacheControl = new CacheControlHeaderValue
        {
            MaxAge = cacheTime,
            Public = true,
        };

        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(cacheTime.Value).SetSize((content?.Length ?? 0) + cacheKey.Length);

        cache.Cache.Set(cacheKey,
            new CacheEntry(content, headers.ContentType, headers.Date, cacheTime.Value, content != null),
            cacheEntryOptions);

        if (content == null)
        {
            return NotFound("No such feed");
        }

        return content;
    }

    private class CacheEntry
    {
        public readonly string? Content;
        public readonly MediaTypeHeaderValue? ContentType;
        public readonly DateTimeOffset? ContentTime;
        public readonly TimeSpan ClientCacheTime;
        public readonly bool Success;

        public CacheEntry(string? content, MediaTypeHeaderValue? contentType, DateTimeOffset? contentTime,
            TimeSpan clientCacheTime, bool success)
        {
            Content = content;
            ContentType = contentType;
            ContentTime = contentTime;
            ClientCacheTime = clientCacheTime;
            Success = success;
        }
    }
}
