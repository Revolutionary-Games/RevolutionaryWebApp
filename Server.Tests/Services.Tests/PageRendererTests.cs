namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
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

    // TODO: also make a test with youtube at the start and verify
    [Fact]
    public void PageRenderer_FeedPreviewHasNoUselessPAtStart()
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();
        linkConverter.GetGeneratedAndProxyImagePrefix().Returns("https://example.com/prefix");

        var renderer = CreateRenderer(linkConverter);

        var page = new VersionedPage("Example post 1")
        {
            PublishedAt = publishedAt,
            LatestContent =
                "[puImage]2022-01-01[/puImage]\n\nSome content for this post.\nThat is spread in multiple lines." +
                "\n\nAnd some more.",
            Type = PageType.Post,
            Visibility = PageVisibility.Public,
        };

        var (result, previewImage) =
            renderer.RenderPreview(page, "https://example.com/", "https://example.com/somePage", 100);

        Assert.Equal("<p>Some content for this post.\nThat is spread in multiple lines.</p><p>And some more.</p>",
            result);
        Assert.Equal("https://example.com/prefix/generated/puBanner/2022-01-01", previewImage);
    }

    private PageRenderer CreateRenderer(IMediaLinkConverter linkConverter)
    {
        var bbCodeService = new MarkdownBbCodeService(linkConverter);
        var markdownService = new MarkdownService(sanitizer, bbCodeService);

        return new PageRenderer(emptyConfiguration, markdownService, linkConverter);
    }
}
