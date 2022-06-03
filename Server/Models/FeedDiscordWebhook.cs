namespace ThriveDevCenter.Server.Models;

using SmartFormat;

/// <summary>
///   Discord webhook to post any new updates to a feed to
/// </summary>
public class FeedDiscordWebhook
{
    public FeedDiscordWebhook(long feedId, string webhookUrl)
    {
        FeedId = feedId;
        WebhookUrl = webhookUrl;
    }

    public long FeedId { get; set; }

    public string WebhookUrl { get; set; }

    /// <summary>
    ///   If set used as a template string to create a different than the default text content when sending the message
    /// </summary>
    public string? CustomItemFormat { get; set; }

    public Feed Feed { get; set; } = null!;

    public string GetMessage(ParsedFeedItem feedItem, string? overrideFeedName = null)
    {
        overrideFeedName ??= feedItem.OriginalFeed ?? "unknown";
        return Smart.Format(CustomItemFormat ?? "{Title} posted by {Author} at {PublishedAt:g}, read it here: {Link}",
            feedItem.GetFormatterData(overrideFeedName));
    }
}
