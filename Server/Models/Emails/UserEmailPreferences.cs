namespace RevolutionaryWebApp.Server.Models.Emails;

using System;
using System.ComponentModel.DataAnnotations;
using DevCenterCommunication.Models;
using Models;

/// <summary>
///   Email preferences attached to a registered user.
/// </summary>
public class UserEmailPreferences : EmailPreferences, ITimestamped
{
    /// <summary>
    ///   Each user can have just one preferences-object
    /// </summary>
    [Key]
    public long UserId { get; set; }

    public User? User { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
