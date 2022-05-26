namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using ModelVerifiers;

public class RepoForReleaseStatsDTO : IIdentifiable
{
    [Required]
    [MustContain("/")]
    [MaxLength(400)]
    public string QualifiedName { get; set; } = string.Empty;

    [IsRegex(AllowBlank = true)]
    public string? IgnoreDownloads { get; set; }

    public bool ShownInAll { get; set; }

    public long Id => QualifiedName.GetHashCode();
}
