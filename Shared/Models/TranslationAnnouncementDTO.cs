namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   A project announcement with a mandatory expiry time.
/// </summary>
public sealed class TranslationAnnouncementDTO
{
    [Required]
    [StringLength(2000, MinimumLength = 10)]
    public string Message { get; set; } = string.Empty;

    public SiteNoticeType Severity { get; set; } = SiteNoticeType.Info;

    [Required]
    public DateTime? EndsAt { get; set; }
}
