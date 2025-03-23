namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using StackExchange.Redis;

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

    private readonly IConnectionMultiplexer cache;
    private readonly IHttpClientFactory httpClientFactory;

    public ImageProxyController(IConnectionMultiplexer cache, IHttpClientFactory httpClientFactory)
    {
        this.cache = cache;
        this.httpClientFactory = httpClientFactory;
    }

    [HttpGet("youtubeThumbnail/{id}")]
    public async Task<IActionResult> YoutubeThumbnail(string id)
    {
        var key = new RedisKey("ImageProxy:YoutubeThumbnail:" + id);

        var database = cache.GetDatabase();

        var existing = await database.StringGetAsync(key);

        if (!existing.IsNullOrEmpty)
        {
            // Detect failure
            var data = existing.ToString();
            if (data == "FAIL")
            {
                Response.Headers.CacheControl = new StringValues("public, max-age=500");
                return Problem("Could not retrieve image from YouTube");
            }

            var stream = new MemoryStream(Convert.FromBase64String(data));
            return File(stream, "image/jpeg");
        }

        var url = string.Format(YoutubeImageUrl, id);
        var backupUrl = string.Format(YoutubeImageBackupUrl, id);

        using var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync(url);

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "image/jpeg")
        {
            return await OnImageSuccessfullyRetrieved(response, key, database);
        }

        response = await client.GetAsync(backupUrl);

        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "image/jpeg")
        {
            return await OnImageSuccessfullyRetrieved(response, key, database);
        }

        Response.Headers.CacheControl = new StringValues("public, max-age=500");

        // Save the failure result to cache in case someone bombards our endpoints with invalid IDs
        await database.StringSetAsync(key, new RedisValue("FAIL"), TimeSpan.FromSeconds(300));

        return Problem("Could not retrieve image from YouTube");
    }

    [NonAction]
    private async Task<IActionResult> OnImageSuccessfullyRetrieved(HttpResponseMessage response, RedisKey key,
        IDatabase database)
    {
        var data = await response.Content.ReadAsByteArrayAsync();

        await database.StringSetAsync(key, Convert.ToBase64String(data), YoutubeCacheTime);

        Response.Headers.CacheControl = new StringValues("public, max-age=3600");
        return File(data, "image/jpeg");
    }
}
