namespace RevolutionaryWebApp.Shared.Tests.Services;

using NSubstitute;
using Shared.Models.Pages;
using Shared.Services;
using Xunit;

public class MarkdownBbCodeServiceTests
{
    [Fact]
    public void BbCode_ImageLinkIsReplaced()
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        linkConverter.TranslateImageLink(".png", "c815f12d-604c-4e2d-a8ce-6b03992d0046", Arg.Any<MediaFileSize>())
            .Returns("https://example.com/c815f12d-604c-4e2d-a8ce-6b03992d0046.png");

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted =
            bbCodeService.PreParseContent("![alt text here](media:png:c815f12d-604c-4e2d-a8ce-6b03992d0046)");

        Assert.Equal("![alt text here](https://example.com/c815f12d-604c-4e2d-a8ce-6b03992d0046.png)", converted);
    }

    [Fact]
    public void BbCode_ImageLinkWithDotInExtensionWorks()
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        linkConverter.TranslateImageLink(".png", "c815f12d-604c-4e2d-a8ce-6b03992d0046", Arg.Any<MediaFileSize>())
            .Returns("https://example.com/c815f12d-604c-4e2d-a8ce-6b03992d0046.png");

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted =
            bbCodeService.PreParseContent("![alt text here](media:.png:c815f12d-604c-4e2d-a8ce-6b03992d0046)");

        Assert.Equal("![alt text here](https://example.com/c815f12d-604c-4e2d-a8ce-6b03992d0046.png)", converted);
    }

    [Theory]
    [InlineData("![alt](media:jpg:c815f12d-654c-4e2d-a8ce-6b03992d0046)",
        "![alt](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.jpg)")]
    [InlineData("![](media:jpg:c815f12d-654c-4e2d-a8ce-6b03992d0046)",
        "![](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.jpg)")]
    [InlineData("![alt](media:png:c815f12d-654c-4e2d-a8ce-6b03992d0046 'Extra comment')",
        "![alt](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.png 'Extra comment')")]
    [InlineData("[![alt](media:png:c815f12d-654c-4e2d-a8ce-6b03992d0046 'Extra comment')](https://extra.link)",
        "[![alt](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.png 'Extra comment')](https://extra.link)")]
    [InlineData("[![alt](media:png:c815f12d-654c-4e2d-a8ce-6b03992d0046)](https://extra.link)",
        "[![alt](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.png)](https://extra.link)")]
    [InlineData("this is text with ![image](media:png:c815f12d-654c-4e2d-a8ce-6b03992d0046) in the middle",
        "this is text with ![image](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.png) in the middle")]
    [InlineData("![2025-03-13_13.55.22.4537.png](media:png:2c26ba15-f944-4693-b2c5-76d3dbebb70e)",
        "![2025-03-13_13.55.22.4537.png](https://example.com/2c26ba15-f944-4693-b2c5-76d3dbebb70e.png)")]
    public void BbCode_ImageLinkWorks(string raw, string expected)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        linkConverter.TranslateImageLink(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MediaFileSize>())
            .Returns(c => $"https://example.com/{c.ArgAt<string>(1)}{c.ArgAt<string>(0)}");

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted = bbCodeService.PreParseContent(raw);

        Assert.Equal(expected, converted);

        // Make sure the post-process doesn't mess with things
        Assert.Equal(expected, bbCodeService.PostProcessContent(converted));
    }

    [Theory]
    [InlineData("![alt](https://img.example.com/stuff.jpg)")]
    [InlineData("![](https://img.example.com/stuff.jpg)")]
    [InlineData("![](https://img.example.com/stuff.png)")]
    [InlineData("![media alternative text](https://img.example.com/stuff.png)")]
    [InlineData("![media:png:c815f12d-654c-4e2d-a8ce-6b03992d0046](https://img.example.com/stuff.png)")]
    [InlineData("embedded ![alt](https://img.example.com/stuff.jpg) image in text")]
    public void BbCode_NormalImageLinksAreUntouched(string raw)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted = bbCodeService.PreParseContent(raw);

        Assert.Equal(raw, converted);

        linkConverter.DidNotReceive()
            .TranslateImageLink(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MediaFileSize>());
    }

    [Theory]
    [InlineData("[puImage]2025-07-05[/puImage]",
        "![Progress Update Banner 2025-07-05](/generated/puBanner/2025-07-05)")]
    [InlineData("# A nice PU\n[puImage]2025-08-05[/puImage]\n\nwith some content",
        "# A nice PU\n![Progress Update Banner 2025-08-05](/generated/puBanner/2025-08-05)\n\nwith some content")]
    [InlineData("[puImage][/puImage]", "[puImage][/puImage]")]
    [InlineData("[puImage]a[/puImage]", "[puImage]a[/puImage]")]
    [InlineData("[rand]2025-08-05[/rand]", "[rand]2025-08-05[/rand]")]
    public void BbCode_CustomBbCodeWorks(string raw, string expected)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted = bbCodeService.PreParseContent(raw);

        Assert.Equal(expected, converted);
    }

    [Theory]
    [InlineData("[puImage][/puImage]", "[puImage][/puImage]")]
    [InlineData("[puImage]a[/puImage]", "[puImage]a[/puImage]")]
    [InlineData("[rand]2025-08-05[/rand]", "[rand]2025-08-05[/rand]")]
    public void BbCode_CustomBbCodeDoesNotReplaceUnintendedStuff(string raw, string expected)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted = bbCodeService.PreParseContent(raw);

        Assert.Equal(expected, converted);
    }

    [Theory]
    [InlineData("[link](page:progress-update-07-05-2025)",
        "[link](https://www.example.com/progress-update-07-05-2025)")]
    [InlineData("[a description](page:another-page)",
        "[a description](https://www.example.com/another-page)")]
    [InlineData("](page:another-page",
        "](https://www.example.com/another-page")]
    [InlineData("([a description](page:another-page)", "([a description](https://www.example.com/another-page)")]
    [InlineData("[a description](page:another-page)](", "[a description](https://www.example.com/another-page)](")]
    [InlineData("(", "(")]
    [InlineData("](", "](")]
    public void BbCode_PageLinksWork(string raw, string expected)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        linkConverter.GetInternalPageLinkPrefix().Returns("https://www.example.com");

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted = bbCodeService.PreParseContent(raw);

        Assert.Equal(expected, converted);
    }

    [Fact]
    public void BbCode_GeneratedImagePrefixWorks()
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        linkConverter.GetGeneratedAndProxyImagePrefix().Returns("/testPrefix");

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted = bbCodeService.PreParseContent("[puImage]2025-07-05[/puImage]");

        Assert.Equal("![Progress Update Banner 2025-07-05](/testPrefix/generated/puBanner/2025-07-05)", converted);
    }

    [Theory]
    [InlineData("<iframe>", "&lt;iframe&gt;")]
    [InlineData("so<iframe>it is", "so&lt;iframe&gt;it is")]
    [InlineData("<iframe>with stuff</iframe>", "&lt;iframe&gt;with stuff&lt;/iframe&gt;")]
    public void BbCode_UnknownIFramesAreRemoved(string raw, string expected)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted = bbCodeService.PreParseContent(raw);

        Assert.Equal(expected, converted);
    }

    [Theory]
    [InlineData("[youtube]PFaX-MuNgfI[/youtube]",
        "<div class=\"youtube-placeholder\" data-video-id=\"PFaX-MuNgfI\">\n    " +
        "<div class=\"thumbnail-container\">\n        <img class=\"youtube-thumbnail\" " +
        "alt=\"YouTube Video Thumbnail\" \n            src=\"/imageProxy/youtubeThumbnail/PFaX-MuNgfI\"/>\n        " +
        "<div class=\"play-button-overlay\">\n            <div class=\"css-play-button\"></div>\n        </div>\n    " +
        "</div>\n    <div class=\"youtube-controls\">\n        <div class=\"controls-row\">\n            " +
        "Viewing embedded videos on this page uses YouTube cookies.\n            You accept these third-party and " +
        "tracking cookies by clicking play.\n        </div>\n        <div class=\"controls-row\">\n            " +
        "<a class=\"youtube-btn open-youtube\" href=\"https://www.youtube.com/watch?v=PFaX-MuNgfI\" " +
        "\n                target=\"_blank\">View on YouTube</a>\n            " +
        "<button class=\"youtube-btn accept-cookies\">Always Accept YouTube Cookies</button>\n        </div>\n    " +
        "</div>\n    <div class=\"youtube-embed-container\" style=\"display: none;\"></div>\n</div>" +
        "<div class=\"youtube-placeholder-below\"></div>\n")]
    public void BbCode_YoutubeRenderingWorks(string raw, string expected)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var markdownService = new MarkdownService(new HtmlSanitizerService(), bbCodeService);

        var converted = markdownService.MarkdownToHtmlWithAllFeatures(raw);

        Assert.Equal(expected, converted);
    }

    /// <summary>
    ///   This tests a function that didn't really end up being useful
    /// </summary>
    [Fact]
    public void BbCode_ImageLinkSplitWorks()
    {
        var converter = new DummyConverter();

        Assert.True(((IMediaLinkConverter)converter).TryParseImageLink("media:png:c815f12d-604c-4e2d-a8ce-6b03992d0046",
            out var imageType, out var imageGlobalId));

        Assert.Equal(".png", imageType);
        Assert.Equal("c815f12d-604c-4e2d-a8ce-6b03992d0046", imageGlobalId);
    }

    private class DummyConverter : IMediaLinkConverter
    {
        public string TranslateImageLink(string imageType, string globalId, MediaFileSize size)
        {
            throw new System.NotImplementedException();
        }

        public string GetGeneratedAndProxyImagePrefix()
        {
            throw new System.NotImplementedException();
        }

        public string GetInternalPageLinkPrefix()
        {
            throw new System.NotImplementedException();
        }
    }
}
