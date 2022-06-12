namespace ThriveDevCenter.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public class CombinedFeedInfo : ClientSideTimedModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public TimeSpan? CacheTime { get; set; }

    public int MaxItems { get; set; }
    public int CombinedFromFeedsCount { get; set; }
    public int DeletedCombinedFromFeedsCount { get; set; }
}
