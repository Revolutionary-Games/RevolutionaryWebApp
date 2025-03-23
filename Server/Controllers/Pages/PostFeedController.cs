namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Services;
using Shared.Models.Pages;
using Utilities;

/// <summary>
///   RSS feed for the published posts
/// </summary>
[Route("feed")]
public class PostFeedController : Controller
{
    private const int FeedItemsToShow = 20;
    private const bool IndentFeedContent = true;

    private readonly ApplicationDbContext database;
    private readonly IPageRenderer pageRenderer;

    private readonly Uri baseUrl;
    private readonly Uri wwwSiteAssetsBaseUrl;

    public PostFeedController(IConfiguration configuration, ApplicationDbContext database, IPageRenderer pageRenderer)
    {
        this.database = database;
        this.pageRenderer = pageRenderer;
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

        return File(stream.ToArray(), "application/atom+xml; charset=utf-8");
    }

    [NonAction]
    private async Task<SyndicationFeed> GenerateFeed()
    {
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

        var items = new List<SyndicationItem>();

        var posts = await database.VersionedPages.AsNoTracking()
            .Where(p => p.Type == PageType.Post && p.Visibility == PageVisibility.Public && !p.Deleted &&
                p.PublishedAt != null).OrderByDescending(p => p.PublishedAt).Take(FeedItemsToShow).ToListAsync();

        foreach (var post in posts)
        {
            if (string.IsNullOrEmpty(post.Permalink))
                throw new Exception("Published page shouldn't be able to not have a permalink");

            if (post.PublishedAt == null)
                throw new Exception("DB returned item not matching the query (published at is null)");

            var postLink = new Uri(baseUrl, post.Permalink);

            items.Add(new SyndicationItem(post.Title, post.GeneratePreview(pageRenderer, postLink),
                postLink, postLink.ToString(), post.UpdatedAt)
            {
                PublishDate = post.PublishedAt.Value,
            });
        }

        feed.Items = items;

        return feed;
    }
}
