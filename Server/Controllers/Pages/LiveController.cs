namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
    private const int NewsPerPage = 10;

    private readonly ApplicationDbContext database;
    private readonly IPageRenderer pageRenderer;
    private readonly CustomMemoryCache cache;
    private readonly Uri? liveCDNBase;
    private readonly string serverName;

    public LiveController(IConfiguration configuration, ApplicationDbContext database, IPageRenderer pageRenderer,
        CustomMemoryCache cache)
    {
        this.database = database;
        this.pageRenderer = pageRenderer;
        this.cache = cache;

        liveCDNBase = configuration.GetLiveWWWBaseUrl();
        serverName = configuration["ServerName"] ?? "server";

        if (string.IsNullOrEmpty(serverName))
            serverName = "server";
    }

    [NonAction]
    public static async Task<List<SiteLayoutPart>> GetSiteLayoutParts(ApplicationDbContext database, PageType pageType)
    {
        // TODO: wiki handling

        return await database.SiteLayoutParts.AsNoTracking().Where(l => l.Enabled).OrderBy(l => l.Order).ToListAsync();
    }

    [NonAction]
    public static string CacheKeyForNewsFeedPage(int page)
    {
        if (page <= 0)
            page = 1;

        return "NewsFeedPageRender_" + page;
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

        if (CheckIfShouldRedirectToCDN(permalink, out var actionResult))
            return actionResult ?? Problem("Unexpected redirect handling error");

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

            // And then fallback to rendering a not found page
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

        var key = "RenderedPageVeryShortCache_" + page.Id;

        // Do a very short memory caching time to avoid someone trying to intentionally go past the cache
        if (cache.Cache.TryGetValue(key, out var cached) && cached is RenderedPage cachedRender)
        {
            return View("Pages/_LivePage", cachedRender);
        }

        // TODO: try to find in redis and only render if not rendered in the past 15 minutes (note when added page
        // updates should flush the cache)

        var realPageParts = await GetSiteLayoutParts(database, page.Type);

        var rendered = await pageRenderer.RenderPage(page, realPageParts, true, timer);

        SetCanonicalUrl(permalink, rendered);

        rendered.OpenGraphPageType = page.GetOpenGraphType();

        // Caching time has to be way lower than 15 seconds as that's how long after a page edit the CDN is purged
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(9))
            .SetPriority(CacheItemPriority.Low).SetSize(rendered.RenderedHtml.Length + 200);

        cache.Cache.Set(key, rendered, cacheEntryOptions);

        return View("Pages/_LivePage", rendered);
    }

    [HttpGet("news/{page:int=0}")]
    [HttpGet("news/page/{page:int}")]
    [HttpGet("news/page/{page:int}/")]
    public async Task<IActionResult> GetNewsFeedPage(int page = 0)
    {
        if (page < 1)
            page = 1;

        var permalink = $"news/{page}";

        if (CheckIfShouldRedirectToCDN(permalink, out var actionResult))
            return actionResult ?? Problem("Unexpected redirect handling error");

        var timer = new Stopwatch();
        timer.Start();

        var key = CacheKeyForNewsFeedPage(page);

        if (cache.Cache.TryGetValue(key, out var cached) && cached is RenderedPage cachedRender)
        {
            if (cachedRender.Title.Contains("Not Found"))
            {
                SetNewsFeedCacheTime(true);
                HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
            }
            else
            {
                SetNewsFeedCacheTime(false);
            }

            return View("Pages/_LivePage", cachedRender);
        }

        // TODO: redis caching as this is pretty extra expensive due to the way we need to prepare all the posts
        // that fall on this page

        var realPageParts = await GetSiteLayoutParts(database, PageType.NormalPage);

        var pages = await database.VersionedPages.AsNoTracking()
            .Where(p => p.Type == PageType.Post && p.PublishedAt != null && p.Visibility == PageVisibility.Public &&
                !p.Deleted).OrderByDescending(p => p.PublishedAt).Skip((page - 1) * NewsPerPage).Take(NewsPerPage + 1)
            .ToListAsync();

        if (pages.Count < 1)
        {
            // No news found / page number too high
            var notFoundRender = await pageRenderer.RenderPage(new VersionedPage("News Feed (Not Found)")
            {
                Type = PageType.NormalPage,
                Visibility = PageVisibility.Public,
                Permalink = permalink,
                LatestContent = "No news found / page number is too high",
            }, realPageParts, false, timer);

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(3))
                .SetPriority(CacheItemPriority.Low).SetSize(notFoundRender.RenderedHtml.Length + 200);

            cache.Cache.Set(key, notFoundRender, cacheEntryOptions);

            SetNewsFeedCacheTime(true);

            HttpContext.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return View("Pages/_LivePage", notFoundRender);
        }

        // We fetch an extra page to know when there are more pages to view
        bool hasNext = pages.Count > NewsPerPage;

        if (hasNext)
            pages.RemoveAt(pages.Count - 1);

        bool hasPrevious = page > 1;

        // TODO: generate the news feed raw text (including images)

        var newsPageHtml = new StringBuilder(500);
        string? firstImage = null;

        void AddNextPreviousNavigation()
        {
            if (!hasNext && !hasPrevious)
                return;

            newsPageHtml.Append("<div style=\"width: 100%\">");

            if (hasPrevious)
            {
                newsPageHtml.Append(
                    $"<a href=\"/live/news/page/{page - 1}\" style=\"float: left\" class=\"news-feed-item\">" +
                    "&larr; Older posts</a>");
            }

            if (hasNext)
            {
                newsPageHtml.Append(
                    $"<a href=\"/live/news/page/{page + 1}\" style=\"float: right\" class=\"news-feed-item\">" +
                    "Newer posts &rarr;</a>");
            }

            newsPageHtml.Append("</div>");
        }

        AddNextPreviousNavigation();

        foreach (var pageInFeed in pages)
        {
            if (pageInFeed.Permalink == null)
            {
                newsPageHtml.Append("<p>Permalink not set</p>");
                continue;
            }

            string pageLink;

            if (liveCDNBase != null)
            {
                pageLink = new Uri(liveCDNBase, pageInFeed.Permalink).ToString();
            }
            else
            {
                pageLink = $"/live/{pageInFeed.Permalink}";
            }

            var (preview, previewImage) = pageRenderer.RenderPreview(pageInFeed, pageLink, 500);

            newsPageHtml.Append("<h2 style=\"margin-bottom 0; border-bottom: 1px solid #6ACDEB;\">");
            newsPageHtml.Append($"<a href=\"{pageLink}\" class=\"news-feed-item\">{pageInFeed.Title}</a></h2>\n");

            if (pageInFeed.PublishedAt == null)
            {
                newsPageHtml.Append("<p>PUBLISHED AT MISSING</p>");
            }
            else
            {
                newsPageHtml.Append("<div class=\"posted-at-time\">Posted on ");
                newsPageHtml.Append($"<a href=\"{pageLink}\" class=\"news-feed-item\">");
                newsPageHtml.Append(
                    pageInFeed.PublishedAt.Value.ToString("MMMM dd, yyyy", CultureInfo.InvariantCulture));
                newsPageHtml.Append("</a>");
                newsPageHtml.Append("</div>\n");
            }

            if (previewImage != null)
            {
                newsPageHtml.Append("<div style=\"display: flex; justify-content: center; margin: 5px;\">");
                newsPageHtml.Append(
                    $"<img src=\"{previewImage}\" alt=\"page preview image for {pageInFeed.Permalink}\"/>\n");
                newsPageHtml.Append("</div>\n");
            }

            newsPageHtml.Append(preview);
            newsPageHtml.Append("<br/>\n");

            if (previewImage != null)
                firstImage = previewImage;
        }

        newsPageHtml.Append("<br/>\n");
        AddNextPreviousNavigation();

        SetNewsFeedCacheTime(false);

        var (sidebar, top, socials) = pageRenderer.ProcessLayoutParts(realPageParts, "news");

        var rendered = new RenderedPage(page > 1 ? $"News Feed (page {page})" : "News Feed", newsPageHtml.ToString(),
            pages[0].PublishedAt ?? DateTime.UtcNow, timer.Elapsed)
        {
            OpenGraphPageType = "website",
            OpenGraphMetaDescription = "News feed for Revolutionary Games Studio",
            PreviewImage = firstImage,
            ByServer = serverName,
            TopNavigation = top,
            Sidebar = sidebar,
            Socials = socials,
        };

        SetCanonicalUrl(permalink, rendered);

        // Lowered cache time in debug mode
#if DEBUG
        var fullRenderCacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(1))
            .SetPriority(CacheItemPriority.High).SetSize(rendered.RenderedHtml.Length + 200);
#else
        var fullRenderCacheOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromMinutes(1))
            .SetPriority(CacheItemPriority.High).SetSize(rendered.RenderedHtml.Length + 200);
#endif

        cache.Cache.Set(key, rendered, fullRenderCacheOptions);

        return View("Pages/_LivePage", rendered);
    }

    private void SetNewsFeedCacheTime(bool notFound)
    {
        if (notFound)
        {
            HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
            {
                MaxAge = TimeSpan.FromMinutes(5),
                Public = true,
            }.ToString();
            return;
        }

        // News feed updates on the CDN every 15 minutes
#if DEBUG
        HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
        }.ToString();
#else
        HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
        {
            MaxAge = TimeSpan.FromMinutes(15),
            Public = true,
        }.ToString();
#endif
    }

    [NonAction]
    private bool CheckIfShouldRedirectToCDN(string permalink, out IActionResult? actionResult)
    {
        if (!HttpContext.Request.Headers.TryGetValue("X-Pull", out var values) || values.Count < 1 ||
            string.IsNullOrWhiteSpace(values[0]))
        {
            // Force redirect to CDN if a CDN is configured for live pages
            if (liveCDNBase != null)
            {
                actionResult = RedirectPermanent(new Uri(liveCDNBase, permalink).ToString());
                return true;
            }
        }

        actionResult = null;
        return false;
    }

    [NonAction]
    private void SetCanonicalUrl(string permalink, RenderedPage rendered)
    {
        // Set canonical URL for the live site
        string? canonicalUrl = null;
        if (liveCDNBase != null)
        {
            canonicalUrl = new Uri(liveCDNBase, permalink).ToString();
        }

        rendered.CanonicalUrl = canonicalUrl;
    }
}
