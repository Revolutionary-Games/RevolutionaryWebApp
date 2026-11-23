namespace RevolutionaryWebApp.Client.Tests.Utilities;

using Client.Utilities;
using Xunit;

public class YoutubeUrlParserTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/watch?feature=shared&v=dQw4w9WgXcQ&t=42")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=example")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/shorts/dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/live/dQw4w9WgXcQ?feature=share")]
    [InlineData("  https://youtu.be/dQw4w9WgXcQ  ")]
    [InlineData("youtube.com/watch?v=dQw4w9WgXcQ")]
    public void ExtractsVideoIdFromCommonVideoUrls(string url)
    {
        Assert.True(YoutubeUrlParser.TryGetVideoId(url, out var videoId));
        Assert.Equal("dQw4w9WgXcQ", videoId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com.example.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com@evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/playlist?list=dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?v=short")]
    [InlineData("https://youtube.com/watch?v=dQw4w9WgXcQ&v=another")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ/extra")]
    [InlineData("https://youtu.be/dQw4w9WgXc!")]
    [InlineData("ftp://youtube.com/watch?v=dQw4w9WgXcQ")]
    public void RejectsUrlsThatCannotProduceValidVideoEmbeds(string? url)
    {
        Assert.False(YoutubeUrlParser.TryGetVideoId(url, out var videoId));
        Assert.Equal(string.Empty, videoId);
    }
}
