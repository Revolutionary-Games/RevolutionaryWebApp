namespace RevolutionaryWebApp.Server.Models.Emails;

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Shared;
using SharedBase.Utilities;

/// <summary>
///   Email preferences for a direct email address without a user account.
/// </summary>
[Index(nameof(Email), IsUnique = true)]
[Index(nameof(NormalizedEmail), IsUnique = true)]
public class DirectEmailPreferences : EmailPreferences
{
    [Required]
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    ///   Normalized version of <see cref="Email"/> used for lookups and uniqueness.
    /// </summary>
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string? NormalizedEmail { get; set; }
}
