namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;

public sealed class UserReportDTO : IIdentifiable
{
    public long Id { get; set; }
    public long? ReporterUserId { get; set; }
    public long? TargetUserId { get; set; }
    public string TargetUserName { get; set; } = string.Empty;
    public UserReportTarget Target { get; set; }
    public long TargetId { get; set; }
    public UserReportReason Reason { get; set; }
    public string Details { get; set; } = string.Empty;
    public string ContentSnapshot { get; set; } = string.Empty;
    public string? ContentPath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public long? ResolvedByUserId { get; set; }
    public UserReportResolution? Resolution { get; set; }
    public List<UserReportNoteDTO> Notes { get; set; } = new();
    public DateTime? FirstContributedAt { get; set; }
    public bool CanPurge { get; set; }
    public int DefaultPurgeHours { get; set; }
}

public sealed class CreateUserReportRequest
{
    public UserReportTarget Target { get; set; }
    public long TargetId { get; set; }
    public UserReportReason Reason { get; set; }

    [MaxLength(4000)]
    public string Details { get; set; } = string.Empty;
}

public sealed class ResolveUserReportRequest
{
    public UserReportResolution Resolution { get; set; }
    public ModerationAction Action { get; set; }

    [Range(1, 3650)]
    public int Days { get; set; } = 7;

    public bool Permanent { get; set; }

    [Range(1, 128)]
    public int? PurgeHours { get; set; }

    public bool AllTime { get; set; }
    public bool DeleteContent { get; set; }

    [Required]
    [MaxLength(4000)]
    public string Note { get; set; } = string.Empty;
}

public sealed class UserReportNoteRequest
{
    [Required]
    [MaxLength(4000)]
    public string Note { get; set; } = string.Empty;
}

public sealed class UserReportNoteDTO
{
    public long? AuthorUserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}