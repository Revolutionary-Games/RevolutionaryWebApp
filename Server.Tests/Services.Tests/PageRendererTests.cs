namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Models.Pages;
using Server.Services;
using Shared.Models.Pages;
using Shared.Services;
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
        Assert.Equal(expectedImage, previewImage);
    }

    [Theory]
    [InlineData("[steam]1779200[/steam]", "[steam]")]
    [InlineData("[thriveItch]", "[thriveItch]")]
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
            renderer.RenderPage(page, "https://example.com/", [], false, new Stopwatch());

        Assert.DoesNotContain(notExpected, result.RenderedHtml);
    }

    private PageRenderer CreateRenderer(IMediaLinkConverter linkConverter)
    {
        var bbCodeService = new MarkdownBbCodeService(linkConverter);
        var markdownService = new MarkdownService(sanitizer, bbCodeService);

        return new PageRenderer(emptyConfiguration, markdownService, linkConverter);
    }
}
