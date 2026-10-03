namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

public sealed class TranslationSyncStatusDTO
{
    public int PendingTranslations { get; set; }
    public DateTime? OldestPendingAt { get; set; }
    public int? PullRequestNumber { get; set; }
    public bool PullRequestOpen { get; set; }

    [MaxLength(30)]
    public string? LastRunStatus { get; set; }

    [MaxLength(4000)]
    public string? LastRunError { get; set; }
}
