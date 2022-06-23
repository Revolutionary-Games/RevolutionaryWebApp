namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Notifications;
using SmartFormat;
using Utilities;

/// <summary>
///   Feed combined from multiple
/// </summary>
[Index(nameof(Name), IsUnique = true)]
public class CombinedFeed : FeedBase, IUpdateNotifications
{
    public CombinedFeed(string name, string htmlFeedItemEntryTemplate) : base(name)
    {
        HtmlFeedItemEntryTemplate = htmlFeedItemEntryTemplate;
    }

    [Required]
    [UpdateFromClientRequest]
    public string HtmlFeedItemEntryTemplate { get; set; }

    [Required]
    [UpdateFromClientRequest]
    public TimeSpan CacheTime { get; set; }

    public ICollection<Feed> CombinedFromFeeds { get; set; } = new HashSet<Feed>();

    public void ProcessContent(IEnumerable<Feed> dataSources)
    {
        // Skip deleted feeds that have no content (in case someone managed to make such a thing)
        var allItems = dataSources.Where(s => !s.Deleted && string.IsNullOrEmpty(s.LatestContent)).SelectMany(s =>
                s.ParseContent(s.LatestContent ?? throw new ArgumentException("feed doesn't have latest content"),
                    out _))
            .OrderByDescending(i => i.PublishedAt).Take(MaxItems);

        var builder = new StringBuilder();

        foreach (var item in allItems)
        {
            builder.Append(Smart.Format(HtmlFeedItemEntryTemplate, item.GetFormatterData(Name)));
        }

        var newContent = builder.ToString();

        if (newContent != LatestContent)
        {
            ContentUpdatedAt = DateTime.UtcNow;
            LatestContent = newContent;
        }
    }

    public CombinedFeedInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            MaxItems = MaxItems,
            CombinedFromFeedsCount = CombinedFromFeeds.Select(f => f.Id).Count(),
            DeletedCombinedFromFeedsCount = CombinedFromFeeds.Where(f => f.Deleted).Select(f => f.Name).Count(),
            CacheTime = CacheTime,
        };
    }

    public CombinedFeedDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            MaxItems = MaxItems,
            CombinedFromFeeds = CombinedFromFeeds.Select(f => f.Id).ToList(),
            DeletedCombinedFromFeeds = CombinedFromFeeds.Where(f => f.Deleted).Select(f => f.Name).ToList(),
            HtmlFeedItemEntryTemplate = HtmlFeedItemEntryTemplate,
            CacheTime = CacheTime,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(
            new CombinedFeedListUpdated() { Type = entityState.ToChangeType(), Item = GetInfo() },
            NotificationGroups.CombinedFeedListUpdated);

        yield return new Tuple<SerializedNotification, string>(
            new CombinedFeedUpdated() { Item = GetDTO() }, NotificationGroups.CombinedFeedUpdatedPrefix + Id);
    }
}
