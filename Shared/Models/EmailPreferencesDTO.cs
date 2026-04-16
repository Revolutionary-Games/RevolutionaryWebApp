namespace RevolutionaryWebApp.Shared.Models;

/// <summary>
///   DTO representing email preference flags used by both server and client.
/// </summary>
public class EmailPreferencesDTO
{
    public bool DisableAllEmails { get; set; }

    public bool AllowSiteAnnouncement { get; set; }
    public bool AllowPasswordReset { get; set; }
    public bool AllowConfirmEmail { get; set; }
    public bool AllowNotifications { get; set; }
    public bool AllowPushBuildStatus { get; set; }
    public bool AllowCommitBuildStatus { get; set; }
}
