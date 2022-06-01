namespace ThriveDevCenter.Shared.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ModelVerifiers;

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

    public int MaxItems { get; set; }

    public int MaxItemLength { get; set; }
    public int? LatestContentLength { get; set; }
    public DateTime? ContentUpdatedAt { get; set; }

    [MaxLength(50)]
    public List<FeedPreprocessingAction>? PreprocessingActions { get; set; }

    [StringLength(5000, MinimumLength = 1)]
    public string? HtmlFeedItemEntryTemplate { get; set; } = string.Empty;

    [StringLength(50, MinimumLength = 2)]
    public string? HtmlFeedVersionSuffix { get; set; }
}
