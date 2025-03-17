namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Models;
using StackExchange.Redis;
using Utilities;

/// <summary>
///   Generates images dynamically on request, for example, progress update banners
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Route("live/generated")]
public class ImageGeneratorController : Controller
{
    private const int ProgressUpdateBannerCacheTime = 3600 * 24 * 7;
    private const int AvatarCacheTime = 3600 * 24;

    private readonly ILogger<ImageGeneratorController> logger;
    private readonly IConnectionMultiplexer cache;
    private readonly ApplicationDbContext database;
    private readonly ImageGenerator imageGenerator;

    public ImageGeneratorController(ILogger<ImageGeneratorController> logger, IConnectionMultiplexer cache,
        ApplicationDbContext database, ImageGenerator imageGenerator)
    {
        this.logger = logger;
        this.cache = cache;
        this.database = database;
        this.imageGenerator = imageGenerator;
    }

    [HttpGet("puBanner/{date}")]
    public async Task<IActionResult> ProgressUpdateBanner(string date)
    {
        DateTime parsedDate;

        try
        {
            parsedDate = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);
        }
        catch (FormatException)
        {
            return BadRequest("Invalid date format");
        }

        var key = new RedisKey("ImageGenerator:PUBanner:" + date);
        var cacheDatabase = cache.GetDatabase();
        var value = await cacheDatabase.StringGetAsync(key);

        if (!value.IsNullOrEmpty)
        {
            var result = value.ToString();
            if (result == "FAIL")
            {
                Response.Headers.CacheControl = new StringValues("public, max-age=500");
                return NotFound();
            }

            Response.Headers.CacheControl = new StringValues($"public, max-age={ProgressUpdateBannerCacheTime}");
            return File(value.ToString(), "image/webp");
        }

        // Ensure the image is asked for a valid date so that we aren't infinitely generating all the different
        // possibilities
        var now = DateTime.UtcNow;
        var diff = now - parsedDate;

        if (diff > -TimeSpan.FromDays(31) && diff < TimeSpan.FromDays(31))
        {
            // It's fine to generate
        }
        else
        {
            // Check the database if there is a page for the date
            var minTime = parsedDate - TimeSpan.FromMinutes(30);
            var maxTime = parsedDate + TimeSpan.FromDays(1);
            if (!await database.VersionedPages.AnyAsync(p => p.PublishedAt > minTime && p.PublishedAt < maxTime))
            {
                logger.LogWarning("PU image requested for invalid date: {Date}", date);

                // Write negative check to cache to ensure we don't do a ton of DB lookups
                await cacheDatabase.StringSetAsync(key, "FAIL", TimeSpan.FromMinutes(15));

                Response.Headers.CacheControl = new StringValues("public, max-age=500");
                return BadRequest("PU image cannot be requested for the given date");
            }
        }

        // Not cached, so we needed to generate a new one

        var imageBytes = await imageGenerator.GenerateProgressUpdateBanner(parsedDate);

        await cacheDatabase.StringSetAsync(key, imageBytes, TimeSpan.FromHours(8));

        Response.Headers.CacheControl = new StringValues($"public, max-age={ProgressUpdateBannerCacheTime}");

        // Return the image as a WebP file
        return File(imageBytes, "image/webp");
    }

    [HttpGet("letterAvatar/{name}")]
    public async Task<IActionResult> LetterAvatar(string name)
    {
        // Generate info from the name
        var initials = ImageGenerator.GetInitials(name);
        var backgroundColor = ColourHelpers.GenerateBackgroundColor(name);

        // Cache key that *may* be shared with multiple names, though not the most likely
        var key = new RedisKey("ImageGenerator:LetterAvatar:" + initials + ":" + backgroundColor.ToHex());
        var cacheDatabase = cache.GetDatabase();
        var value = await cacheDatabase.StringGetAsync(key);

        if (!value.IsNullOrEmpty)
        {
            Response.Headers.CacheControl = new StringValues($"public, max-age={AvatarCacheTime}");
            return File(value.ToString(), "image/webp");
        }

        // Not cached, so we need to generate a new one
        var imageBytes = await imageGenerator.GenerateLetterAvatar(initials, backgroundColor);

        await cacheDatabase.StringSetAsync(key, imageBytes, TimeSpan.FromHours(4));

        Response.Headers.CacheControl = new StringValues($"public, max-age={AvatarCacheTime}");

        // Return the image as a WebP file
        return File(imageBytes, "image/webp");
    }
}
