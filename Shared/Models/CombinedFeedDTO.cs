namespace ThriveDevCenter.Shared.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using DevCenterCommunication.Models;

public class CombinedFeedDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public DateTime? ContentUpdatedAt { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxItems { get; set; }

    [Required]
    [MaxLength(10)]
    [MinLength(2)]
    public List<long> CombinedFromFeeds { get; set; } = new();

    /// <summary>
    ///   Lists feeds by name that are deleted that this depends on and won't get new data
    /// </summary>
    public List<string>? DeletedCombinedFromFeeds { get; set; }

    [Required]
    [StringLength(5000, MinimumLength = 1)]
    public string HtmlFeedItemEntryTemplate { get; set; } = string.Empty;

    [Required]
    public TimeSpan CacheTime { get; set; }

    public CombinedFeedDTO Clone()
    {
        return new CombinedFeedDTO
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Name = Name,
            MaxItems = MaxItems,
            CombinedFromFeeds = CombinedFromFeeds.ToList(),
            DeletedCombinedFromFeeds = DeletedCombinedFromFeeds?.ToList(),
            HtmlFeedItemEntryTemplate = HtmlFeedItemEntryTemplate,
            CacheTime = CacheTime,
        };
    }
}
