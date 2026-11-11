namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

/// <summary>
///   Durable deletion work committed with a moderation purge, including a second pass after upload URLs expire.
/// </summary>
[Index(nameof(CompletedAt), nameof(DeleteAfter))]
public class ModerationUploadDeletion
{
    public long Id { get; set; }

    [MaxLength(2000)]
    public string Path { get; set; } = string.Empty;

    public ModerationUploadStorage Storage { get; set; }
    public DateTime DeleteAfter { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
