namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Models.Pages;
using Services;
using Shared;
using Shared.Models.Pages;
using Utilities;

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

        liveCDNBase = configuration.GetLiveWWWBaseUrl();
    }

    [NonAction]
    public static async Task<List<SiteLayoutPart>> GetSiteLayoutParts(ApplicationDbContext database, PageType pageType)
    {
        // TODO: wiki handling

        return await database.SiteLayoutParts.AsNoTracking().Where(l => l.Enabled).OrderBy(l => l.Order).ToListAsync();
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
            // If page not found, then try redirects first

            // Redirects don't use trailing slashes
            permalink = permalink.TrimEnd('/');

            // Try to find a redirect
            var redirect = await database.PageRedirects.AsNoTracking()
                .FirstOrDefaultAsync(r => r.FromPath == permalink);

            if (redirect != null)
            {
                HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
                {
                    MaxAge = AppInfo.PageRedirectCacheTime,
                    Public = true,
                }.ToString();

                var target = redirect.GetTarget(liveCDNBase);

                return Redirect(target);
            }

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

            var parts = await GetSiteLayoutParts(database, PageType.Post);

            // For a normal missing page, render a not found page
            HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;

            return View("Pages/_LivePage", pageRenderer.RenderNotFoundPage(parts, timer));
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

        var realPageParts = await GetSiteLayoutParts(database, page.Type);

        var rendered = await pageRenderer.RenderPage(page, realPageParts, true, timer);

        rendered.CanonicalUrl = canonicalUrl;

        rendered.OpenGraphPageType = page.GetOpenGraphType();

        return View("Pages/_LivePage", rendered);
    }
}
