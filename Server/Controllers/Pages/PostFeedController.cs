namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Models;
using Services;
using Shared.Models.Pages;
using Utilities;

/// <summary>
///   RSS feed for the published posts
/// </summary>
[Route("feed")]
[Route("live/feed")]
public class PostFeedController : Controller
{
    private const int FeedItemsToShow = 20;
    private const bool IndentFeedContent = true;

    private readonly ApplicationDbContext database;
    private readonly IPageRenderer pageRenderer;
    private readonly CustomMemoryCache cache;

    private readonly Uri baseUrl;
    private readonly Uri wwwSiteAssetsBaseUrl;

    public PostFeedController(IConfiguration configuration, ApplicationDbContext database, IPageRenderer pageRenderer,
        CustomMemoryCache cache)
    {
        this.database = database;
        this.pageRenderer = pageRenderer;
        this.cache = cache;
        baseUrl = configuration.GetLiveWWWBaseUrl() ?? new Uri(configuration.GetBaseUrl(), "live/");
        wwwSiteAssetsBaseUrl = configuration.GetWWWAssetBaseUrl();
    }

    // The plain "HttpGet" defines which is the default feed format

    [HttpGet("rss.xml")]
    [OutputCache(Duration = 900)]
    public async Task<IActionResult> GetRss()
    {
        var feed = await GenerateFeed();

        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            NewLineHandling = NewLineHandling.Entitize,
            NewLineOnAttributes = true,
            Indent = IndentFeedContent,
            Async = true,
        };

        using var stream = new MemoryStream();
        await using (var xmlWriter = XmlWriter.Create(stream, settings))
        {
            var rssFormatter = new Rss20FeedFormatter(feed, false);
            rssFormatter.WriteTo(xmlWriter);
            await xmlWriter.FlushAsync();
        }

        // Try setting an extra cache control header, though with OutputCache this seems to not have an effect
        HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
        {
            MaxAge = TimeSpan.FromMinutes(15),
            Public = true,
        }.ToString();

        return File(stream.ToArray(), "application/rss+xml; charset=utf-8");
    }

    [HttpGet]
    [HttpGet("atom.xml")]
    [OutputCache(Duration = 900)]
    public async Task<IActionResult> GetAtom()
    {
        var feed = await GenerateFeed();

        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            NewLineHandling = NewLineHandling.Entitize,
            NewLineOnAttributes = true,
            Indent = IndentFeedContent,
            Async = true,
        };

        using var stream = new MemoryStream();
        await using (var xmlWriter = XmlWriter.Create(stream, settings))
        {
            var rssFormatter = new Atom10FeedFormatter(feed);
            rssFormatter.WriteTo(xmlWriter);
            await xmlWriter.FlushAsync();
        }

        HttpContext.Response.Headers.CacheControl = new CacheControlHeaderValue
        {
            MaxAge = TimeSpan.FromMinutes(15),
            Public = true,
        }.ToString();

        return File(stream.ToArray(), "application/atom+xml; charset=utf-8");
    }

    [NonAction]
    private async Task<SyndicationFeed> GenerateFeed()
    {
        var cacheKey = "PostSyndicationFeed";

        // This extra caching is probably not completely needed as the endpoints above are already cached, but this
        // memory cache is probably inexpensive enough to not cause any problems
        if (cache.Cache.TryGetValue(cacheKey, out var existingFeedRaw) &&
            existingFeedRaw is SyndicationFeed existingFeed)
        {
            return existingFeed;
        }

        int length = 50 + wwwSiteAssetsBaseUrl.ToString().Length * 2;

        var feed = new SyndicationFeed("Revolutionary Games Studio News",
            "News feed for Revolutionary Games Studio",
            baseUrl,
            "RevolutionaryGamesStudioNews",
            DateTime.UtcNow)
        {
            Copyright = new TextSyndicationContent($"{DateTime.UtcNow.Year} Revolutionary Games Studio"),
            TimeToLive = TimeSpan.FromMinutes(60),
            Generator = "RevolutionaryWebApp/" +
                (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"),

            // TODO: make sure this is right in production
            ImageUrl = new Uri(wwwSiteAssetsBaseUrl, "favicon.ico"),
        };

        var author = new SyndicationPerson("revolutionarygamesstudio@gmail.com", "Revolutionary Games",
            baseUrl.ToString());

        var items = new List<SyndicationItem>();

        var posts = await database.VersionedPages.AsNoTracking()
            .Where(p => p.Type == PageType.Post && p.Visibility == PageVisibility.Public && !p.Deleted &&
                p.PublishedAt != null).OrderByDescending(p => p.PublishedAt).Take(FeedItemsToShow).ToListAsync();

        // Set to false if no full text should be in the feed
        bool first = true;

        foreach (var post in posts)
        {
            if (string.IsNullOrEmpty(post.Permalink))
                throw new Exception("Published page shouldn't be able to not have a permalink");

            if (post.PublishedAt == null)
                throw new Exception("DB returned item not matching the query (published at is null)");

            var postLink = new Uri(baseUrl, post.Permalink);

            // The first post gets full content here to give some balance between data usage and letting RSS readers
            // to read the full post in the client without opening a browser
            int previewLength;
            SyndicationContent preview;

            if (first)
            {
                (preview, previewLength) = await post.GenerateFullSyndicationContent(pageRenderer, postLink);
            }
            else
            {
                preview = post.GeneratePreview(pageRenderer, postLink, out previewLength);
            }

            length += previewLength + post.Permalink.Length + post.Title.Length;

            var item = new SyndicationItem(post.Title, preview, postLink, postLink.ToString(), post.UpdatedAt)
            {
                PublishDate = post.PublishedAt.Value,

                // TODO: allow overriding per post
                Authors = { author },
            };

            if (first)
            {
                item.Summary = post.GeneratePreview(pageRenderer, postLink, out var extraLength);
                length += extraLength;
            }

            items.Add(item);

            first = false;
        }

        feed.Items = items;

        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(TimeSpan.FromSeconds(60)).SetPriority(CacheItemPriority.Low).SetSize(length);

        cache.Cache.Set(cacheKey, feed, cacheEntryOptions);

        return feed;
    }
}
