namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using SharedBase.Utilities;

/// <summary>
///   Represents a pending local account signup that is waiting for the user to
///   confirm their email address before the actual <see cref="User"/> is created.
/// </summary>
public class PendingUserSignup : BaseModel
{
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public required string Email { get; set; } = string.Empty;

    [MaxLength(GlobalConstants.MaxEmailLength)]
    public required string NormalizedEmail { get; set; } = string.Empty;

    /// <summary>
    ///   Random token that is embedded in the email link for completing the signup.
    /// </summary>
    [MaxLength(64)]
    public required string Token { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    ///   When this pending signup was created.
    /// </summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    ///   When a verification email was last sent (for rate limiting / re-sends later on).
    /// </summary>
    public DateTime? LastEmailSentUtc { get; set; }

    /// <summary>
    ///   How many signup emails have been sent so far for this pending signup. Used to
    ///   rate-limit repeated requests so this flow can't be abused to spam an address.
    /// </summary>
    public int SendCount { get; set; }
}
