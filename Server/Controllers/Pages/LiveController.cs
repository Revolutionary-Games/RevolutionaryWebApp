namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Services;
using Shared;
using Shared.Models.Pages;

/// <summary>
///   Serves live, rendered, versions of static main site pages and news posts
/// </summary>
[Route("live")]
public class LiveController : Controller
{
    private readonly ApplicationDbContext database;
    private readonly IPageRenderer pageRenderer;
    private readonly Uri? liveCDNBase;

    public LiveController(IConfiguration configuration, ApplicationDbContext database, IPageRenderer pageRenderer)
    {
        this.database = database;
        this.pageRenderer = pageRenderer;

        var cdn = configuration["CDN:LiveUrl"];
        if (!string.IsNullOrWhiteSpace(cdn))
        {
            liveCDNBase = new Uri(cdn);
        }
        else
        {
            liveCDNBase = null;
        }
    }

    [HttpGet("{*permalink}")]
    public async Task<IActionResult> GetPage([Required] string permalink)
    {
        // Automatically map home page to special index value to fetch
        if (string.IsNullOrWhiteSpace(permalink))
            permalink = AppInfo.IndexPermalinkName;

        // Asking for a URL ending in '/' is the same as asking without it
        permalink = permalink.TrimEnd('/');

        var timer = new Stopwatch();
        timer.Start();

        if (!HttpContext.Request.Headers.TryGetValue("X-Pull", out var values) || values.Count < 1 ||
            string.IsNullOrWhiteSpace(values[0]))
        {
            // Force redirect to CDN if a CDN is configured for live pages
            if (liveCDNBase != null)
            {
                return RedirectPermanent(new Uri(liveCDNBase, permalink).ToString());
            }
        }

        var page = await database.VersionedPages.AsNoTracking().FirstOrDefaultAsync(p =>
            p.Permalink == permalink && (p.Type == PageType.NormalPage || p.Type == PageType.Post) &&
            p.Visibility == PageVisibility.Public && !p.Deleted);

        if (page == null)
        {
            HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
            {
                MaxAge = AppInfo.NotFoundPageCacheTime,
                Public = true,
            }.ToString();

            // Don't allow asking for stuff that looks like files from this endpoint (but only if a page was not found
            // this ensures that
            if (new Regex(@"\w+\.\w+$").IsMatch(permalink))
            {
                return NotFound("Site resources should not be attempted to be fetched from this URL");
            }

            // For normal missing page, render a not found page
            HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;

            return View("Pages/_LivePage", pageRenderer.RenderNotFoundPage(timer));
        }

        // TODO: try to find in redis and only render if not rendered in the past 15 minutes (note when added page
        // updates should flush the cache)

        // No caching in debug mode
#if DEBUG
        HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
        }.ToString();
#else
        HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
        {
            MaxAge = page.CalculatedDesiredCacheTime(),
            Public = true,
        }.ToString();
#endif

        // Set canonical URL for the live site
        string? canonicalUrl = null;
        if (liveCDNBase != null)
        {
            canonicalUrl = new Uri(liveCDNBase, permalink).ToString();
        }

        var rendered = await pageRenderer.RenderPage(page, timer);

        rendered.CanonicalUrl = canonicalUrl;

        return View("Pages/_LivePage", rendered);
    }
}
