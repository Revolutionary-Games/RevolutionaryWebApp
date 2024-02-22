namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Used to keep track of what feed items have been seen in a feed to not process items multiple times
/// </summary>
public class SeenFeedItem
{
    public SeenFeedItem(long feedId, string itemIdentifier)
    {
        FeedId = feedId;
        ItemIdentifier = itemIdentifier;
    }

    public long FeedId { get; set; }

    /// <summary>
    ///   The identifier that identifies this item uniquely within a feed
    /// </summary>
    public string ItemIdentifier { get; set; }

    [Required]
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;

    public Feed Feed { get; set; } = null!;
}
