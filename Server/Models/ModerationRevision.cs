namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

/// <summary>
///   Scalar before/after images for contributions that otherwise have no reversible history.
/// </summary>
[Index(nameof(UserId), nameof(CreatedAt))]
[Index(nameof(CreatedAt))]
public class ModerationRevision
{
    /// <summary>
    ///   How long to keep all model changes so that moderation can undo them. This is not permanent to save on DB size
    /// </summary>
    public const int RetentionMonths = 12;

    public long Id { get; set; }
    public long? UserId { get; set; }
    public User? User { get; set; }

    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;

    // NOTE: these fields don't have max lengths as we need to be able to store any changed DB data in them
    public string KeyJson { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevertedAt { get; set; }
}
