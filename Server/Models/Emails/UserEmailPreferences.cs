namespace RevolutionaryWebApp.Server.Models.Emails;

using Models;

/// <summary>
///   Email preferences attached to a registered user.
/// </summary>
public class UserEmailPreferences : EmailPreferences
{
    public long UserId { get; set; }

    public User? User { get; set; }
}
