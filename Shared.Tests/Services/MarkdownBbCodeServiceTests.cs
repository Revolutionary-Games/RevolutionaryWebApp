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
