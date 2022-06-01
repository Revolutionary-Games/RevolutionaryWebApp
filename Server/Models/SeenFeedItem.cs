namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Used to keep track of what feed items have been seen in a feed to not process items multiple times
/// </summary>
public class SeenFeedItem
{
    public SeenFeedItem(Feed feed, string itemIdentifier)
    {
        FeedId = feed.Id;
        Feed = feed;
        ItemIdentifier = itemIdentifier;
    }

    [Key]
    public long FeedId { get; set; }

    /// <summary>
    ///   The identifier that identifies this item uniquely within a feed
    /// </summary>
    [Key]
    public string ItemIdentifier { get; set; }

    [Required]
    public DateTime SeenAt { get; set; } = DateTime.UtcNow;
    
    public Feed Feed { get; set; }
}
