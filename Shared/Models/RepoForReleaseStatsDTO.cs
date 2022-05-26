namespace ThriveDevCenter.Shared.Models;

using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using ModelVerifiers;

public class RepoForReleaseStatsDTO
{
    public RepoForReleaseStatsDTO(string qualifiedName)
    {
        QualifiedName = qualifiedName;
    }

    [Required]
    [MustContain("/")]
    [MaxLength(400)]
    public string QualifiedName { get; set; }

    [IsRegex]
    public string? IgnoreDownloads { get; set; }

    public bool ShownInAll { get; set; }
}
