namespace ThriveDevCenter.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Controllers;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

public class CiBuild : IUpdateNotifications
{
    public long CiProjectId { get; set; }

    [AllowSortingBy]
    public long CiBuildId { get; set; }

    /// <summary>
    ///   The hash of the commit triggering this build. The build can also contain other commits
    /// </summary>
    [Required]
    public string CommitHash { get; set; } = string.Empty;

    /// <summary>
    ///   Reference to the remote ref we need to checkout to run this build
    /// </summary>
    [Required]
    public string RemoteRef { get; set; } = string.Empty;

    /// <summary>
    ///   The branch detected from RemoteRef
    /// </summary>
    public string? Branch { get; set; }

    /// <summary>
    ///   When true the RemoteRef is from the current repository (and not a fork). This determines what build
    ///   secrets, and caches are used for a build (PRs from forks are given less permissions for security reasons)
    /// </summary>
    public bool IsSafe { get; set; }

    /// <summary>
    ///   When this build was started / created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    public BuildStatus Status { get; set; } = BuildStatus.Running;

    public CiProject? CiProject { get; set; }

    public ICollection<CiJob> CiJobs { get; set; } = new HashSet<CiJob>();

    public string? PreviousCommit { get; set; }

    /// <summary>
    ///   The commit message of the (latest) commit
    /// </summary>
    public string? CommitMessage { get; set; }

    /// <summary>
    ///   Json serialized list of GithubCommits that github reported as being part of the push
    ///   that created this build
    /// </summary>
    public string? Commits { get; set; }

    [Timestamp]
    public uint Version { get; set; }

    [NotMapped]
    public List<GithubCommit>? ParsedCommits
    {
        get => Commits != null ? JsonSerializer.Deserialize<List<GithubCommit>>(Commits) : null;
        set
        {
            if (value == null)
            {
                Commits = null;
                return;
            }

            Commits = JsonSerializer.Serialize(value);
        }
    }

    public CIBuildDTO GetDTO()
    {
        return new()
        {
            CiProjectId = CiProjectId,
            CiBuildId = CiBuildId,
            CommitHash = CommitHash,
            RemoteRef = RemoteRef,
            CreatedAt = CreatedAt,
            Status = Status,
            ProjectName = CiProject?.Name ?? CiProjectId.ToString(),
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        var dto = GetDTO();

        yield return new Tuple<SerializedNotification, string>(new CIProjectBuildsListUpdated
        {
            Type = entityState.ToChangeType(),
            Item = dto,
        }, NotificationGroups.CIProjectBuildsUpdatedPrefix + CiProjectId);

        var notificationsId = CiProjectId + "_" + CiBuildId;

        yield return new Tuple<SerializedNotification, string>(new CIBuildUpdated
        {
            Item = dto,
        }, NotificationGroups.CIProjectsBuildUpdatedPrefix + notificationsId);
    }
}
