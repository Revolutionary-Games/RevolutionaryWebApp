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

    [Theory]
    [InlineData("![alt](media:jpg:c815f12d-654c-4e2d-a8ce-6b03992d0046)",
        "![alt](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.jpg)")]
    [InlineData("![alt](media:png:c815f12d-654c-4e2d-a8ce-6b03992d0046 'Extra comment')",
        "![alt](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.png 'Extra comment')")]
    [InlineData("[![alt](media:png:c815f12d-654c-4e2d-a8ce-6b03992d0046 'Extra comment')](https://extra.link)",
        "[![alt](https://example.com/c815f12d-654c-4e2d-a8ce-6b03992d0046.png 'Extra comment')](https://extra.link)")]
    public void BbCode_ImageLinkWorks(string raw, string expected)
    {
        var linkConverter = Substitute.For<IMediaLinkConverter>();

        linkConverter.TranslateImageLink(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MediaFileSize>())
            .Returns(c => $"https://example.com/{c.ArgAt<string>(1)}{c.ArgAt<string>(0)}");

        var bbCodeService = new MarkdownBbCodeService(linkConverter);

        var converted = bbCodeService.PreParseContent(raw);

        Assert.Equal(expected, converted);

        // Make sure post-process doesn't mess with things
        Assert.Equal(expected, bbCodeService.PostProcessContent(converted));
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
    }
}
