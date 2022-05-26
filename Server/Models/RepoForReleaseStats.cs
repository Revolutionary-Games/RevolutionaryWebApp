namespace ThriveDevCenter.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Shared.Models;

/// <summary>
///   Configured repository to get stats for from the download stats API
/// </summary>
public class RepoForReleaseStats
{
    public RepoForReleaseStats(string qualifiedName, bool showInAll)
    {
        QualifiedName = qualifiedName;
        ShowInAll = showInAll;
    }

    [Key]
    public string QualifiedName { get; set; }

    /// <summary>
    ///   Ignore counting downloads that match this regex in the total counts
    /// </summary>
    public string? IgnoreDownloads { get; set; }

    /// <summary>
    ///   If true this is included in the all downloads endpoint, otherwise this needs to be manually queried
    /// </summary>
    public bool ShowInAll { get; set; }

    public RepoForReleaseStatsDTO GetDTO()
    {
        return new(QualifiedName)
        {
            IgnoreDownloads = IgnoreDownloads,
            ShownInAll = ShowInAll,
        };
    }
}
