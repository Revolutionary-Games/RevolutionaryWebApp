namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using DevCenterCommunication.Models;
using FeedParser.Shared.Models;
using SharedBase.ModelVerifiers;

public class FeedDTO : ClientSideTimedModel
{
    public bool Deleted { get; set; }

    [Required]
    [StringLength(1024, MinimumLength = 5)]
    [MustContain("http")]
    public string Url { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public TimeSpan PollInterval { get; set; }
    public TimeSpan? CacheTime { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxItems { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxItemLength { get; set; }

    public int? LatestContentLength { get; set; }
    public DateTime? ContentUpdatedAt { get; set; }

    [MaxLength(50)]
    public List<FeedPreprocessingAction>? PreprocessingActions { get; set; }

    [StringLength(5000)]
    public string? HtmlFeedItemEntryTemplate { get; set; } = string.Empty;

    [StringLength(50)]
    public string? HtmlFeedVersionSuffix { get; set; }

    public FeedDTO Clone()
    {
        return new FeedDTO
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
            LatestContentLength = LatestContentLength,
            ContentUpdatedAt = ContentUpdatedAt,
            PreprocessingActions = PreprocessingActions?.ToList(),
            HtmlFeedItemEntryTemplate = HtmlFeedItemEntryTemplate,
            HtmlFeedVersionSuffix = HtmlFeedVersionSuffix,
        };
    }
}
