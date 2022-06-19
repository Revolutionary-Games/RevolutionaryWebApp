namespace ThriveDevCenter.Server.Models;

using Microsoft.EntityFrameworkCore;

[Index(nameof(FeedId), nameof(ItemId), IsUnique = true)]
public class ProcessedFeedItem
{
    public ProcessedFeedItem(long feedId, string itemId)
    {
        FeedId = feedId;
        ItemId = itemId;
    }

    public long FeedId { get; set; }

    public string ItemId { get; set; }
}
