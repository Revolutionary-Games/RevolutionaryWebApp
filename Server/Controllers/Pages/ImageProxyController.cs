namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Primitives;

/// <summary>
///   Handles requesting images on behalf of clients and returning them. This helps with hiding client IPs and
///   potentially tracking cookies.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Route("live/imageProxy")]
public class ImageProxyController : Controller
{
    private const string YoutubeImageUrl = "https://img.youtube.com/vi/{0}/maxresdefault.jpg";
    private const string YoutubeImageBackupUrl = "https://img.youtube.com/vi/{0}/default.jpg";

    private static readonly TimeSpan YoutubeCacheTime = TimeSpan.FromHours(8);

    private readonly RedisCache cache;
    private readonly IHttpClientFactory httpClientFactory;

    public ImageProxyController(RedisCache cache, IHttpClientFactory httpClientFactory)
    {
        this.cache = cache;
        this.httpClientFactory = httpClientFactory;
    }

    [HttpGet("youtubeThumbnail/{id}")]
    public async Task<IActionResult> YoutubeThumbnail(string id)
    {
        var key = "ImageProxy:YoutubeThumbnail:" + id;

        var existing = await cache.GetAsync(key);

        if (existing != null)
        {
            // Detect failure
            if (existing.Length <= 10 && Encoding.UTF8.GetString(existing) == "FAIL")
            {
                Response.Headers.CacheControl = new StringValues("public, max-age=500");
                return Problem("Could not retrieve image from YouTube");
            }

            return File(existing, "image/jpeg");
        }

        var url = string.Format(YoutubeImageUrl, id);
        var backupUrl = string.Format(YoutubeImageBackupUrl, id);

        using var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "image/jpeg")
        {
            return await OnImageSuccessfullyRetrieved(response, key);
        }

        response = await client.GetAsync(backupUrl);

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "image/jpeg")
        {
            return await OnImageSuccessfullyRetrieved(response, key);
        }

        Response.Headers.CacheControl = new StringValues("public, max-age=500");

        // Save the failure result to cache in case someone bombards our endpoints with invalid IDs
        await cache.SetAsync(key, Encoding.UTF8.GetBytes("FAIL"), new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = new DateTimeOffset(DateTime.UtcNow, TimeSpan.FromSeconds(300)),
        });

        return Problem("Could not retrieve image from YouTube");
    }

    [NonAction]
    private async Task<IActionResult> OnImageSuccessfullyRetrieved(HttpResponseMessage response, string key)
    {
        var data = await response.Content.ReadAsByteArrayAsync();

        await cache.SetAsync(key, data, new DistributedCacheEntryOptions
        {
            AbsoluteExpiration = new DateTimeOffset(DateTime.UtcNow, YoutubeCacheTime),
        });

        Response.Headers.CacheControl = new StringValues("public, max-age=3600");
        return File(data, "image/jpeg");
    }
}
