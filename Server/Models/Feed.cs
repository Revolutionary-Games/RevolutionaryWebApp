namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using FeedParser.Models;
using FeedParser.Shared.Models;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   RSS feed to download from an external source
/// </summary>
[Index(nameof(Name), IsUnique = true)]
[Index(nameof(ContentUpdatedAt))]
public class Feed : FeedBase, ISoftDeletable, IUpdateNotifications, IDTOCreator<FeedDTO>, IInfoCreator<FeedInfo>, IFeed
{
    public Feed(string url, string name, TimeSpan pollInterval) : base(name)
    {
        Url = url;
        Name = name;
        PollInterval = pollInterval;
    }

    [Required]
    [UpdateFromClientRequest]
    public string Url { get; set; }

    [Required]
    [UpdateFromClientRequest]
    public TimeSpan PollInterval { get; set; }

    /// <summary>
    ///   If specified overrides the cache time passed to readers of this feed data
    /// </summary>
    [UpdateFromClientRequest]
    public TimeSpan? CacheTime { get; set; }

    [UpdateFromClientRequest]
    public string? HtmlFeedItemEntryTemplate { get; set; }

    [UpdateFromClientRequest]
    public string? HtmlFeedVersionSuffix { get; set; }

    public string? HtmlLatestContent { get; set; }

    [UpdateFromClientRequest]
    public int MaxItemLength { get; set; } = int.MaxValue;

    public string? PreprocessingActionsRaw { get; set; }

    [NotMapped]
    [UpdateFromClientRequest]
    public List<FeedPreprocessingAction>? PreprocessingActions
    {
        get => PreprocessingActionsRaw != null ?
            JsonSerializer.Deserialize<List<FeedPreprocessingAction>>(PreprocessingActionsRaw) :
            null;
        set
        {
            PreprocessingActionsRaw = JsonSerializer.Serialize(value);
        }
    }

    public bool Deleted { get; set; }

    public int LatestContentHash { get; set; }

    [Timestamp]
    public uint Version { get; set; }

    public ICollection<SeenFeedItem> SeenFeedItems { get; set; } = new HashSet<SeenFeedItem>();

    public ICollection<FeedDiscordWebhook> DiscordWebhooks { get; set; } = new HashSet<FeedDiscordWebhook>();

    public ICollection<CombinedFeed> CombinedInto { get; set; } = new HashSet<CombinedFeed>();

    public FeedInfo GetInfo()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Deleted = Deleted,
            Url = Url,
            Name = Name,
            PollInterval = PollInterval,
            CacheTime = CacheTime,
            ContentUpdatedAt = ContentUpdatedAt,
            PreprocessingActionsCount = PreprocessingActions?.Count ?? 0,
            HasHtmlFeedItemEntryTemplate = !string.IsNullOrEmpty(HtmlFeedItemEntryTemplate),
            HtmlFeedVersionSuffix = HtmlFeedVersionSuffix,
        };
    }

    public FeedDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Deleted = Deleted,
            Url = Url,
            Name = Name,
            PollInterval = PollInterval,
            CacheTime = CacheTime,
            MaxItems = MaxItems,
            MaxItemLength = MaxItemLength,
            LatestContentLength = LatestContent?.Length,
            ContentUpdatedAt = ContentUpdatedAt,
            PreprocessingActions = PreprocessingActions,
            HtmlFeedItemEntryTemplate = HtmlFeedItemEntryTemplate,
            HtmlFeedVersionSuffix = HtmlFeedVersionSuffix,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        // Skip sending normal updates if this is in deleted state (and didn't currently become undeleted
        // or deleted)
        if (entityState != EntityState.Modified || !Deleted)
        {
            yield return new Tuple<SerializedNotification, string>(
                new FeedListUpdated { Type = entityState.ToChangeType(), Item = GetInfo() },
                NotificationGroups.FeedListUpdated);
        }

        yield return new Tuple<SerializedNotification, string>(
            new FeedUpdated { Item = GetDTO() }, NotificationGroups.FeedUpdatedPrefix + Id);
    }
}
