namespace ThriveDevCenter.Server.Tests.Models.Tests;

using System;
using FeedParser.Models;
using Server.Models;
using Xunit;

public class FeedDiscordWebhookTests
{
    [Fact]
    public static void FeedDiscord_CustomItemFormatWorks()
    {
        var webhook = new FeedDiscordWebhook(1, "hook")
        {
            CustomItemFormat = "New post by {AuthorFirstWord} in topic {Title}\n{Link}",
            Feed = new Feed("test", "test", TimeSpan.FromMinutes(1))
        };

        Assert.Equal("New post by User1 in topic Item title\nhttps://item.link",
            webhook.GetMessage(new ParsedFeedItem("1234", "https://item.link", "Item title", "User1")));
    }

    [Fact]
    public static void FeedDiscord_DefaultItemFormatWorks()
    {
        var published = DateTime.UtcNow;

        var webhook = new FeedDiscordWebhook(1, "hook")
        {
            Feed = new Feed("test", "test", TimeSpan.FromMinutes(1)),
        };

        Assert.Equal($"Item title posted by User1 at {published:g}, read it here: https://item.link",
            webhook.GetMessage(new ParsedFeedItem("1234", "https://item.link", "Item title", "User1")
            {
                PublishedAt = published,
            }));
    }
}
