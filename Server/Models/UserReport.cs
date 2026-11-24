namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[Index(nameof(ResolvedAt), nameof(CreatedAt))]
[Index(nameof(Target), nameof(TargetId), nameof(ResolvedAt))]
public class UserReport : IUpdateNotifications
{
    [AllowSortingBy]
    public long Id { get; set; }
    public long? ReporterUserId { get; set; }
    public User? ReporterUser { get; set; }
    public long? TargetUserId { get; set; }
    public User? TargetUser { get; set; }

    // This must be longer than CommunicationConstants.MAX_USERNAME_LENGTH
    [MaxLength(120)]
    public string TargetUserName { get; set; } = string.Empty;

    public UserReportTarget Target { get; set; }
    public long TargetId { get; set; }
    public UserReportReason Reason { get; set; }

    [MaxLength(4000)]
    public string Details { get; set; } = string.Empty;

    [MaxLength(50000)]
    public string ContentSnapshot { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? ContentPath { get; set; }

    [AllowSortingBy]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ResolvedAt { get; set; }
    public long? ResolvedByUserId { get; set; }
    public User? ResolvedByUser { get; set; }
    public UserReportResolution? Resolution { get; set; }

    [Timestamp]
    public uint Xmin { get; set; }

    public ICollection<UserReportNote> Notes { get; set; } = new List<UserReportNote>();

    public UserReportDTO GetDTO(bool includeContent = true)
    {
        return new UserReportDTO
        {
            Id = Id, ReporterUserId = ReporterUserId, TargetUserId = TargetUserId,
            TargetUserName = TargetUserName, Target = Target, TargetId = TargetId, Reason = Reason,
            Details = includeContent ? Details : string.Empty,
            ContentSnapshot = includeContent ? ContentSnapshot : string.Empty,
            ContentPath = ContentPath, CreatedAt = CreatedAt, ResolvedAt = ResolvedAt,
            ResolvedByUserId = ResolvedByUserId, Resolution = Resolution,
            Notes = includeContent ? Notes.OrderBy(note => note.CreatedAt).Select(note => new UserReportNoteDTO
            {
                AuthorUserId = note.AuthorUserId, Text = note.Text, CreatedAt = note.CreatedAt,
            }).ToList() : new List<UserReportNoteDTO>(),
        };
    }

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new UserReportListUpdated
        {
            Type = entityState.ToChangeType(), Item = GetDTO(false),
        }, NotificationGroups.ModerationQueue);
    }
}

public class UserReportNote : IUpdateNotifications
{
    public long Id { get; set; }
    public long ReportId { get; set; }
    public UserReport Report { get; set; } = null!;
    public long? AuthorUserId { get; set; }
    public User? AuthorUser { get; set; }

    [MaxLength(4000)]
    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public IEnumerable<Tuple<SerializedNotification, string>> GetNotifications(EntityState entityState)
    {
        yield return new Tuple<SerializedNotification, string>(new UserReportListUpdated
        {
            Item = new UserReportDTO { Id = ReportId },
        }, NotificationGroups.ModerationQueue);
    }
}
