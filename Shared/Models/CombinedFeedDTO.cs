namespace ThriveDevCenter.Shared.Models;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

public class CombinedFeedDTO : ClientSideTimedModel
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;

    public int MaxItems { get; set; }

    [Required]
    [MaxLength(10)]
    [MinLength(2)]
    public List<long> CombinedFromFeeds { get; set; } = new();

    [Required]
    [StringLength(5000, MinimumLength = 1)]
    public string HtmlFeedItemEntryTemplate { get; set; } = string.Empty;
}
