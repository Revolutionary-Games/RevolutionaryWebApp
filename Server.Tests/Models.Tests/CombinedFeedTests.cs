namespace RevolutionaryWebApp.Server.Tests.Models.Tests;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using FeedParser.Services;
using FeedParser.Tests.Services.Tests;
using Server.Models;
using Xunit;

public class CombinedFeedTests
{
    [Fact]
    public void CombinedFeed_CreatesContent()
    {
        var part1 = new Feed("test", "test1", TimeSpan.FromMinutes(1))
        {
            MaxItems = 1,
        };
        part1.ProcessContent(ExampleFeedData.TestGithubFeedContent);

        var part2 = new Feed("test", "test2", TimeSpan.FromMinutes(1))
        {
            MaxItems = 1,
        };
        part2.ProcessContent(ExampleFeedData.TestDiscourseFeedContent);

        // LineLengthCheckDisable
        var feed = new CombinedFeed("all", @"<div class=""custom-feed-item-class feed-{FeedName}"">
<span class=""custom-feed-icon-{OriginalFeedName}""></span>
<span class=""custom-feed-title""><span class=""custom-feed-title-main"">
<a class=""custom-feed-title-link"" href=""{Link}"">{Title}</a>
</span><span class=""custom-feed-by""> by
<span class=""custom-feed-author"">{AuthorFirstWord}</span></span><span class=""custom-feed-at""> at <span class=""custom-feed-time"">{PublishedAt:yyyy-dd-MM HH.mm}</span></span>
</span><br><span class=""custom-feed-content"">{Summary}<br><a class=""custom-feed-item-url"" href=""{Link}"">Read it here</a></span></div>
</div>");

        // LineLengthCheckEnable

        feed.ProcessContent(new List<Feed> { part1, part2 });

        Assert.NotNull(feed.LatestContent);
        Assert.NotEmpty(feed.LatestContent!);

        Assert.Equal(2, Regex.Matches(feed.LatestContent!, "custom-feed-content").Count);
        Assert.Contains("system</span>", feed.LatestContent!);

        // Check that it is valid HTML
        var parser = new HtmlParser();
        parser.ParseDocument(feed.LatestContent!);
    }
}
