namespace RevolutionaryWebApp.Server.Models.Emails;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Microsoft.EntityFrameworkCore;
using SharedBase.Utilities;

/// <summary>
///   Email preferences for a direct email address without a user account.
/// </summary>
[Index(nameof(Email), IsUnique = true)]
[Index(nameof(NormalizedEmail), IsUnique = true)]
public class DirectEmailPreferences : EmailPreferences, ITimestampedModel
{
    [Key]
    public long Id { get; set; }

    [Required]
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    ///   Normalized version of <see cref="Email"/> used for lookups and uniqueness.
    /// </summary>
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string? NormalizedEmail { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
