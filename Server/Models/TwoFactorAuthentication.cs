namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;

/// <summary>
///   Authenticator credentials and recovery state. Secrets are protected with ASP.NET data protection.
/// </summary>
public class TwoFactorAuthentication
{
    [Key]
    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public bool Enabled { get; set; }

    [MaxLength(1024)]
    public string? ProtectedSecret { get; set; }

    public DateTime? SetupExpiresAt { get; set; }

    [MaxLength(2048)]
    public string BackupCodeHashes { get; set; } = "[]";

    public long? LastUsedTimeStep { get; set; }

    public int FailedAttempts { get; set; }

    public DateTime? LockedUntil { get; set; }

    public DateTime? RecoveryRequestedAt { get; set; }

    public int RecoveryWarningsSent { get; set; }

    public DateTime? LastRecoveryWarningAt { get; set; }

    /// <summary>
    ///   Invalidates pending logins whenever the authenticator is replaced or disabled.
    /// </summary>
    public Guid KeyVersion { get; set; } = Guid.NewGuid();

    /// <summary>
    ///   Makes code consumption, lockouts, and recovery cancellation safe across server instances.
    /// </summary>
    [ConcurrencyCheck]
    public Guid Revision { get; set; } = Guid.NewGuid();
}
