namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using SmartFormat;
using Utilities;

/// <summary>
///   Discord webhook to post any new updates to a feed to
/// </summary>
[Index(nameof(FeedId), nameof(WebhookUrl), IsUnique = true)]
public class FeedDiscordWebhook : IUpdateNotifications
{
    public FeedDiscordWebhook(long feedId, string webhookUrl)
    {
        FeedId = feedId;
        WebhookUrl = webhookUrl;
    }

    public long FeedId { get; set; }

    [AllowSortingBy]
    public string WebhookUrl { get; set; }

    /// <summary>
    ///   If set used as a template string to create a different than the default text content when sending the message
    /// </summary>
    [UpdateFromClientRequest]
    public string? CustomItemFormat { get; set; }

    public Feed Feed { get; set; } = null!;

    public FeedDiscordWebhookDTO GetDTO()
    {
        return new()
        {
            FeedId = FeedId,
            WebhookUrl = WebhookUrl,
            CustomItemFormat = CustomItemFormat,
        };
    }

    public string GetMessage(ParsedFeedItem feedItem, string? overrideFeedName = null)
    {
        overrideFeedName ??= feedItem.OriginalFeed ?? "unknown";
        return Smart.Format(CustomItemFormat ?? "{Title} posted by {Author} at {PublishedAt:g}, read it here: {Link}",
            feedItem.GetFormatterData(overrideFeedName));
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(
            new FeedDiscordWebhookListUpdated() { Type = entityState.ToChangeType(), Item = GetDTO() },
            NotificationGroups.FeedDiscordWebhookListUpdatedPrefix + FeedId);
    }
}
