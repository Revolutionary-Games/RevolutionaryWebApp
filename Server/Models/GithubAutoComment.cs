namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using Utilities;

[Index(nameof(Condition))]
public class GithubAutoComment : UpdateableModel, IUpdateNotifications
{
    [AllowSortingBy]
    [UpdateFromClientRequest]
    public bool Enabled { get; set; }

    /// <summary>
    ///   Only matching repository PRs are considered. If null, empty or * will post on any repo
    /// </summary>
    [UpdateFromClientRequest]
    public string? Repository { get; set; }

    [Required]
    [UpdateFromClientRequest]
    public string CommentText { get; set; } = string.Empty;

    [AllowSortingBy]
    [UpdateFromClientRequest]
    public AutoCommentCondition Condition { get; set; }

    public ICollection<GithubPullRequest> PostedOnPullRequests { get; set; } = new HashSet<GithubPullRequest>();

    public GithubAutoCommentDTO GetDTO()
    {
        return new()
        {
            Id = Id,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Enabled = Enabled,
            Repository = Repository,
            CommentText = CommentText,
            Condition = Condition,
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(
            new GithubAutoCommentListUpdated { Type = entityState.ToChangeType(), Item = GetDTO() },
            NotificationGroups.GithubAutoCommentListUpdated);
    }
}
