namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shared.Models;
using Shared.Notifications;

/// <summary>
///   Feed combined from multiple
/// </summary>
[Index(nameof(Name), IsUnique = true)]
public class CombinedFeed : UpdateableModel, IUpdateNotifications
{
    public CombinedFeed(string name)
    {
        Name = name;
    }

    /// <summary>
    ///   Name of the combined feed. Needs to be made sure doesn't conflict with <see cref="Feed.Name"/>
    /// </summary>
    [Required]
    public string Name { get; set; }

    public int MaxItems { get; set; } = int.MaxValue;

    public ICollection<Feed> CombinedFromFeeds { get; set; } = new HashSet<Feed>();

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
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(
            new CombinedFeedListUpdated() { Item = GetDTO() },
            NotificationGroups.CombinedFeedListUpdated);
    }
}
