namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

/// <summary>
///   Tracks email bounce information for a single email address. This is used to
///   decide when to temporarily disable emails and when to resume them later.
/// </summary>
[Index(nameof(NormalizedEmail))]
public class EmailBounce : BaseModel
{
    [Key]
    [MaxLength(500)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    ///   Normalized email for wider matching. Should be kept in sync with <see cref="Email"/>.
    /// </summary>
    [MaxLength(500)]
    public string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>
    ///   Count of bounces since last reset/handling.
    /// </summary>
    public int OutstandingBounces { get; set; }

    /// <summary>
    ///   First time a bounce was recorded (for the current series).
    /// </summary>
    public DateTime FirstBounceUtc { get; set; }

    /// <summary>
    ///   Last time a bounce was recorded (for the current series).
    /// </summary>
    public DateTime LastBounceUtc { get; set; }

    /// <summary>
    ///   True when emails were disabled by the bounce handler so that a resume job can
    ///   later safely re-enable them. If the user disables emails manually, this must remain false.
    /// </summary>
    public bool DisabledBySystem { get; set; }

    /// <summary>
    ///   Current backoff in weeks to use by resume scheduling. Increased when disable triggers again.
    ///   Capped at 52 weeks (1 year).
    /// </summary>
    public int BackoffWeeks { get; set; }
}
