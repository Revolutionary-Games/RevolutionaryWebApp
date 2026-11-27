namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Controllers.Pages;
using Server.Models;
using Server.Models.Pages;
using Server.Services;
using Server.Utilities;
using Shared;
using Shared.Models.Pages;
using Shared.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class PageRendererTests
{
    private readonly HtmlSanitizerService sanitizer = new();
    private readonly IConfiguration emptyConfiguration = new ConfigurationBuilder().Build();

    private readonly DateTime publishedAt = new(2022, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("[puImage]2022-01-01[/puImage]\n\nSome content for this post.\nThat is spread in multiple lines." +
        "\n\nAnd some more.",
        "<p>Some content for this post.\nThat is spread in multiple lines.</p><p>And some more.</p>",
        "https://example.com/prefix/generated/puBanner/2022-01-01")]
    [InlineData("[youtube]1wqj45ZsTmk[/youtube]\n\nThis is a post that starts with a YouTube video.",
        "<p>This is a post that starts with a YouTube video.</p>",
        "https://example.com/prefix/imageProxy/youtubeThumbnail/1wqj45ZsTmk")]
    public void PageRenderer_FeedPreviewHasNoUselessPAtStart(string text, string expected, string? expectedImage)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();
        linkConverter.GetGeneratedAndProxyImagePrefix().Returns("https://example.com/prefix");

        var renderer = CreateRenderer(linkConverter);

        var page = new VersionedPage("Example post 1")
        {
            PublishedAt = publishedAt,
            LatestContent = text,
            Type = PageType.Post,
            Visibility = PageVisibility.Public,
        };

        var (result, previewImage) =
            renderer.RenderPreview(page, "https://example.com/", "https://example.com/somePage", 100);

        Assert.Equal(expected, result);
        Assert.Equal(expectedImage, previewImage?.Source);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1700, 902)]
    [InlineData(800, 1600)]
    public async Task PageRenderer_NewsFeedPreservesBannerDimensions(int originalWidth, int originalHeight)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();
        linkConverter.GetGeneratedAndProxyImagePrefix().Returns("/live");

        var banner = "[puImage]2022-01-01[/puImage]";
        var expectedSource = "http://localhost:5000/live/generated/puBanner/2022-01-01";
        var expectedWidth = AppInfo.ProgressUpdateBannerWidth;
        var expectedHeight = AppInfo.ProgressUpdateBannerHeight;
        if (originalWidth > 0)
        {
            var media = new MediaFile("custom.png", Guid.NewGuid(), 1, null)
            {
                Processed = true,
                OriginalWidth = originalWidth,
                OriginalHeight = originalHeight,
            };
            banner = new MediaBrowserEntry(media.GetInfo()).GetMarkdown();
            expectedSource = $"https://media.example.com/{media.GlobalId}/page-fit.webp";
            linkConverter.TranslateImageLink(".webp", media.GlobalId.ToString(), MediaFileSize.FitPage)
                .Returns(expectedSource);

            using var original = new Image<Rgba32>(originalWidth, originalHeight);
            using var pageFit = MediaImagePreviews.CreatePageImage(original);
            expectedWidth = pageFit.Width;
            expectedHeight = pageFit.Height;
        }

        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await database.VersionedPages.AddAsync(new VersionedPage("Progress Update")
        {
            Permalink = "progress-update",
            PublishedAt = publishedAt,
            LatestContent = banner + "\n\nPost text.",
            Type = PageType.Post,
            Visibility = PageVisibility.Public,
        });
        await database.SaveChangesAsync();

        var cache = new CustomMemoryCache();
        using var memoryCache = cache.Cache;
        var controller = new LiveController(emptyConfiguration, database, CreateRenderer(linkConverter), cache)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.Request.Scheme = "http";
        controller.Request.Host = new HostString("localhost:5000");

        var view = Assert.IsType<ViewResult>(await controller.GetNewsFeedPage());
        var feed = Assert.IsType<RenderedPage>(view.Model);
        using var document = new HtmlParser().ParseDocument(feed.RenderedHtml);
        var image = Assert.Single(document.QuerySelectorAll("img"));
        Assert.Equal(expectedSource, image.GetAttribute("src"));
        Assert.Equal(expectedWidth.ToString(CultureInfo.InvariantCulture), image.GetAttribute("width"));
        Assert.Equal(expectedHeight.ToString(CultureInfo.InvariantCulture), image.GetAttribute("height"));
    }

    [Theory]
    [InlineData("[steam]1779200[/steam]", "[steam]")]
    [InlineData("[thriveItch]", "[thriveItch]")]
    [InlineData("[center]", "[center]")]
    public async Task PageRenderer_SpecificMarkdownIsDetected(string text, string notExpected)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();
        linkConverter.GetGeneratedAndProxyImagePrefix().Returns("https://example.com/prefix");

        var renderer = CreateRenderer(linkConverter);

        var page = new VersionedPage("Example post 1")
        {
            PublishedAt = publishedAt,
            LatestContent = text,
            Type = PageType.Post,
            Visibility = PageVisibility.Public,
        };

        var result = await
            renderer.RenderPage(page, "https://example.com/", [], false, null, new Stopwatch());

        Assert.DoesNotContain(notExpected, result.RenderedHtml);
    }

    [Fact]
    public async Task PageRenderer_AddsUniqueHeadingPermalinksToLivePage()
    {
        const string baseUrl = "https://example.com/";
        var renderer = CreateRenderer(Substitute.For<IMediaLinkConverter>());
        var page = new VersionedPage("Guide")
        {
            Permalink = "guide",
            Type = PageType.NormalPage,
            Visibility = PageVisibility.Public,
            LatestContent = """
                # Setup
                ## [Café](https://example.com/) **Guide**
                ### Setup
                #### Setup-2
                ##### Further details
                """,
        };

        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await database.VersionedPages.AddAsync(page);
        await database.SaveChangesAsync();

        var cache = new CustomMemoryCache();
        using var memoryCache = cache.Cache;
        var controller = new LiveController(emptyConfiguration, database, renderer, cache)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        controller.Request.Scheme = "https";
        controller.Request.Host = new HostString("example.com");

        var view = Assert.IsType<ViewResult>(await controller.GetPage("guide"));
        var livePage = Assert.IsType<RenderedPage>(view.Model);
        using var document = new HtmlParser().ParseDocument(livePage.RenderedHtml);

        var headings = document.QuerySelectorAll("h1, h2, h3, h4");
        Assert.Equal(new[] { "section-setup", "section-café-guide", "section-setup-2", "section-setup-2-2" },
            headings.Select(heading => heading.Id));
        Assert.Equal(new[] { "#section-setup", "#section-caf%C3%A9-guide", "#section-setup-2", "#section-setup-2-2" },
            document.QuerySelectorAll(".heading-permalink").Select(link => link.GetAttribute("href")));
        Assert.Equal("Link to Café Guide",
            document.QuerySelector("h2 > a.heading-permalink")?.GetAttribute("aria-label"));
        Assert.Equal("https://example.com/", document.QuerySelector("h2 > a")?.GetAttribute("href"));
        Assert.Null(document.QuerySelector("h5")?.GetAttribute("id"));

        var feedContent = await page.GenerateFullSyndicationContent(renderer, new Uri(baseUrl + "guide"));
        Assert.DoesNotContain("heading-permalink", feedContent.Content.Text);
    }

    [Theory]
    [InlineData("png", "png")]
    [InlineData("webp", "png")]
    [InlineData("webp", "webp")]
    public async Task PageRenderer_RendersOpenGraphAndFullSizeImageMetadata(string previewType, string originalType)
    {
        const string mediaBase = "https://media.example/";
        const string serverBase = "https://server.example/site/";
        var imageId = Guid.NewGuid();
        var largeUrl = $"{mediaBase}{imageId}/large.{originalType}";
        var configuration = new ConfigurationManager
        {
            ["BaseUrl"] = serverBase,
            ["MediaStorage:Download:URL"] = mediaBase,
        };
        var linkConverter = new MediaLinkConverter(configuration);
        var markdownService = new MarkdownService(sanitizer, new MarkdownBbCodeService(linkConverter));
        var renderer = new PageRenderer(configuration, markdownService, linkConverter);

        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await database.MediaFiles.AddAsync(new MediaFile($"screenshot #1.{originalType}", imageId, 1, null)
        {
            Id = 42,
            Processed = true,
            OriginalWidth = 3840,
            OriginalHeight = 2160,
        });
        await database.SaveChangesAsync();

        var page = new VersionedPage("Screenshot")
        {
            LatestContent = $"A screenshot.\n\n![Cover](cover.png)\n\n![Screenshot](media:{previewType}:{imageId})",
        };
        var result = await renderer.RenderPage(page, serverBase, [], true, database, new Stopwatch());
        var rendered = result.RenderedHtml;

        Assert.Equal("A screenshot.", result.OpenGraphMetaDescription);
        Assert.Equal($"{serverBase}cover.png", result.PreviewImage);
        Assert.Contains($"src=\"{mediaBase}{imageId}/page-fit.{previewType}\"", rendered);
        var originalUrl = $"{serverBase}api/v1/MediaFile/view/42?name=screenshot%20%231.{originalType}";
        Assert.Contains($"data-original-src=\"{originalUrl}\"", rendered);
        Assert.Contains($"data-large-src=\"{largeUrl}\"", rendered);
        Assert.Contains("data-original-width=\"3840\"", rendered);
        Assert.Contains("data-original-height=\"2160\"", rendered);
        Assert.DoesNotContain($"{mediaBase}{imageId}/screenshot", rendered);
    }

    private PageRenderer CreateRenderer(IMediaLinkConverter linkConverter)
    {
        var bbCodeService = new MarkdownBbCodeService(linkConverter);
        var markdownService = new MarkdownService(sanitizer, bbCodeService);

        return new PageRenderer(emptyConfiguration, markdownService, linkConverter);
    }
}
