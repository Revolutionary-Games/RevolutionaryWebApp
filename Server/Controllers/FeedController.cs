using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using Models;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class FeedController : Controller
{
    private readonly IConfiguration configuration;
    private readonly ApplicationDbContext database;

    public FeedController(IConfiguration configuration, ApplicationDbContext database)
    {
        this.configuration = configuration;
        this.database = database;
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
    [ResponseCache(VaryByQueryKeys = new[] { "name" })]
    public async Task<ActionResult<string>> GetFeed([Required] string name)
    {
        TimeSpan? cacheTime = null;
        string? content = null;

        // First try to find a feed
        var feed = await database.Feeds.AsNoTracking().FirstOrDefaultAsync(f =>
            !f.Deleted && (f.Name == name ||
                (f.HtmlFeedVersionSuffix != null && f.Name + f.HtmlFeedVersionSuffix == name)));

        var headers = HttpContext.Response.GetTypedHeaders();

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
            }

            if (content != null)
            {
                cacheTime = feed.CacheTime ?? feed.PollInterval;

                if (feed.ContentUpdatedAt != null)
                    headers.ETag = new EntityTagHeaderValue(feed.ContentUpdatedAt.Value.ToString("R"));
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

                    if (combined.ContentUpdatedAt != null)
                        headers.ETag = new EntityTagHeaderValue(combined.ContentUpdatedAt.Value.ToString("R"));
                }
            }
        }

        // Default cache when we don't find anything
        cacheTime ??= TimeSpan.FromSeconds(60);

        headers.CacheControl = new CacheControlHeaderValue()
        {
            MaxAge = cacheTime,
            Public = true,
        };

        if (content == null)
            return NotFound("No such feed");

        return content;
    }
}
