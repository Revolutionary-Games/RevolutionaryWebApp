namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public class FeedInfo : ClientSideTimedModel
{
    public bool Deleted { get; set; }

    [Required]
    public string Url { get; set; } = string.Empty;

    [Required]
    public string Name { get; set; } = string.Empty;

    public TimeSpan PollInterval { get; set; }
    public TimeSpan? CacheTime { get; set; }
    public DateTime? ContentUpdatedAt { get; set; }
    public int PreprocessingActionsCount { get; set; }
    public bool HasHtmlFeedItemEntryTemplate { get; set; }
    public string? HtmlFeedVersionSuffix { get; set; }
}
