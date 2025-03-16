namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
    private const int LetterAvatarSize = 256;

    private const int ProgressUpdateBannerWidth = 1200;
    private const int ProgressUpdateBannerHeight = 630;

    private readonly ILogger<ImageGeneratorController> logger;
    private readonly IConnectionMultiplexer cache;
    private readonly ApplicationDbContext database;
    private readonly IHttpClientFactory httpClientFactory;

    public ImageGeneratorController(ILogger<ImageGeneratorController> logger, IConnectionMultiplexer cache,
        ApplicationDbContext database, IHttpClientFactory httpClientFactory)
    {
        this.logger = logger;
        this.cache = cache;
        this.database = database;
        this.httpClientFactory = httpClientFactory;
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

        using var image = new Image<Rgb24>(ProgressUpdateBannerWidth, ProgressUpdateBannerHeight, Color.Black);

        // Draw initials in the center
        // TODO: customize the font (this should be present on most Linuxes)
        var font = SystemFonts.CreateFont("DejaVu Sans", 96, FontStyle.Bold);

        // TODO: actually implement all the parts and the fancy background image

        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(font)
            {
                TextAlignment = TextAlignment.Center,
                Font = font,
                Origin = new PointF(ProgressUpdateBannerWidth / 2.0f, ProgressUpdateBannerHeight / 2.0f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }, parsedDate.ToString("MMMM dd yyyy", CultureInfo.InvariantCulture), Color.White);
        });

        // Save the image after generating
        using var memoryStream = new MemoryStream();
        await image.SaveAsync(memoryStream, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = 95,
        });

        var imageBytes = memoryStream.ToArray();

        await cacheDatabase.StringSetAsync(key, imageBytes, TimeSpan.FromHours(8));

        // Return the image as a WebP file
        return File(imageBytes, "image/webp");
    }

    [HttpGet("letterAvatar/{name}")]
    [ResponseCache(Duration = 3600, VaryByQueryKeys = new[] { "name" })]
    public async Task<IActionResult> LetterAvatar(string name)
    {
        // Generate info from the name
        var initials = GetInitials(name);
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

        // Use ImageSharp to generate the image
        using var image = new Image<Rgb24>(LetterAvatarSize, LetterAvatarSize, backgroundColor);

        // Draw initials in the center
        // TODO: customize the font (this should be present on most Linuxes)
        var font = SystemFonts.CreateFont("DejaVu Sans", 96, FontStyle.Bold);

        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(font)
            {
                TextAlignment = TextAlignment.Center,
                Font = font,
                Origin = new PointF(LetterAvatarSize / 2.0f, LetterAvatarSize / 2.0f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }, initials, Color.White);
        });

        // Save the image after generating
        using var memoryStream = new MemoryStream();
        await image.SaveAsync(memoryStream, new WebpEncoder
        {
            FileFormat = WebpFileFormatType.Lossless,
            Quality = 80,
        });

        var imageBytes = memoryStream.ToArray();

        await cacheDatabase.StringSetAsync(key, imageBytes, TimeSpan.FromHours(4));

        // Return the image as a WebP file
        return File(imageBytes, "image/webp");
    }

    /// <summary>
    ///   Helper to generate initials (1 or 2 letters)
    /// </summary>
    /// <param name="name">Name / username</param>
    /// <returns>Initials</returns>
    private static string GetInitials(string name)
    {
        var parts = name.Trim().Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "?";

        // Single name
        if (parts.Length == 1)
        {
            return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpper();
        }

        // Use the first letter of the first two words
        return (parts[0][0] + parts[1][0].ToString()).ToUpper();
    }
}
