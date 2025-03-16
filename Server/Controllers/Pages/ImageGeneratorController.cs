namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    [ResponseCache(Duration = 500, VaryByQueryKeys = new[] { "date" })]
    public async Task<IActionResult> ProgressUpdateBanner(string date)
    {
        DateTime parsedDate;

        try
        {
            parsedDate = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToUniversalTime();
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
                return NotFound();

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

                return BadRequest("PU image cannot be requested for the given date");
            }
        }

        // Not cached, so we need to generate a new one

        var imageBytes = await imageGenerator.GenerateProgressUpdateBanner(parsedDate);

        await cacheDatabase.StringSetAsync(key, imageBytes, TimeSpan.FromHours(8));

        // Return the image as a WebP file
        return File(imageBytes, "image/webp");
    }

    [HttpGet("letterAvatar/{name}")]
    [ResponseCache(Duration = 3600, VaryByQueryKeys = new[] { "name" })]
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
            return File(value.ToString(), "image/webp");
        }

        // Not cached, so we need to generate a new one
        var imageBytes = await imageGenerator.GenerateLetterAvatar(initials, backgroundColor);

        await cacheDatabase.StringSetAsync(key, imageBytes, TimeSpan.FromHours(4));

        // Return the image as a WebP file
        return File(imageBytes, "image/webp");
    }
}
