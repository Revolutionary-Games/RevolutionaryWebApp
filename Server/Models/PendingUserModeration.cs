namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

/// <summary>
///   Durable staged ban and optional purge, including a second cleanup pass for in-flight writes.
/// </summary>
[Index(nameof(AppliedAt), nameof(CreatedAt))]
[Index(nameof(CleanedUpAt), nameof(CreatedAt))]
public class PendingUserModeration
{
    public long Id { get; set; }
    public long? TargetUserId { get; set; }
    public User? TargetUser { get; set; }

    public long? ActorUserId { get; set; }
    public User? ActorUser { get; set; }

    public long? ReportId { get; set; }
    public UserReport? Report { get; set; }

    public DateTime BanUntil { get; set; }
    public DateTime? PurgeCutoff { get; set; }

    [MaxLength(4000)]
    public string Reason { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? AppliedAt { get; set; }
    public DateTime? CleanedUpAt { get; set; }
}
