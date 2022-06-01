namespace ThriveDevCenter.Server.Models;

using System.ComponentModel.DataAnnotations;
using SmartFormat;

/// <summary>
///   Discord webhook to post any new updates to a feed to
/// </summary>
public class FeedDiscordWebhook
{
    public FeedDiscordWebhook(Feed feed, string webhookUrl)
    {
        FeedId = feed.Id;
        Feed = feed;
        WebhookUrl = webhookUrl;
    }

    [Key]
    public long FeedId { get; set; }

    [Key]
    public string WebhookUrl { get; set; }

    /// <summary>
    ///   If set used as a template string to create a different than the default text content when sending the message
    /// </summary>
    public string? CustomItemFormat { get; set; }

    public Feed Feed { get; set; }

    public string GetMessage(ParsedFeedItem feedItem, string? overrideFeedName = null)
    {
        overrideFeedName ??= feedItem.OriginalFeed ?? "unknown";
        return Smart.Format(CustomItemFormat ?? "{Title} posted by {Author} at {PublishedAt:g}, read it here: {Link}",
            feedItem.GetFormatterData(overrideFeedName));
    }
}
