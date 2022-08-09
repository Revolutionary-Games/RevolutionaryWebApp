namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

/// <summary>
///   Configured repository to get stats for from the download stats API
/// </summary>
public class RepoForReleaseStats : IUpdateNotifications
{
    public RepoForReleaseStats(string qualifiedName, bool showInAll)
    {
        QualifiedName = qualifiedName;
        ShowInAll = showInAll;
    }

    [Key]
    [AllowSortingBy]
    public string QualifiedName { get; set; }

    /// <summary>
    ///   Ignore counting downloads that match this regex in the total counts
    /// </summary>
    public string? IgnoreDownloads { get; set; }

    /// <summary>
    ///   If true this is included in the all downloads endpoint, otherwise this needs to be manually queried
    /// </summary>
    [AllowSortingBy]
    public bool ShowInAll { get; set; }

    public RepoForReleaseStatsDTO GetDTO()
    {
        return new()
        {
            QualifiedName = QualifiedName,
            IgnoreDownloads = IgnoreDownloads,
            ShownInAll = ShowInAll,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new RepoForReleaseStatsListUpdated { Type = entityState.ToChangeType(), Item = GetDTO() },
            NotificationGroups.RepoForReleaseStatsListUpdated);
    }
}
