namespace RevolutionaryWebApp.Server.Models.Emails;

using Interfaces;
using Models;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using Utilities;

/// <summary>
///   Abstract base class for email preference sets. Concrete implementations are stored in the DB.
/// </summary>
public abstract class EmailPreferences : IDTOCreator<EmailPreferencesDTO>
{
    /// <summary>
    ///   When true, no emails are sent regardless of individual category settings.
    /// </summary>
    [UpdateFromClientRequest]
    public bool DisableAllEmails { get; set; } = false;

    // Per-category allow flags (default to true)
    [UpdateFromClientRequest]
    public bool AllowSiteAnnouncement { get; set; } = true;

    [UpdateFromClientRequest]
    public bool AllowPasswordReset { get; set; } = true;

    [UpdateFromClientRequest]
    public bool AllowConfirmEmail { get; set; } = true;

    [UpdateFromClientRequest]
    public bool AllowNotifications { get; set; } = true;

    [UpdateFromClientRequest]
    public bool AllowPushBuildStatus { get; set; } = true;

    [UpdateFromClientRequest]
    public bool AllowCommitBuildStatus { get; set; } = true;

    public EmailPreferencesDTO GetDTO()
    {
        return new EmailPreferencesDTO
        {
            DisableAllEmails = DisableAllEmails,
            AllowSiteAnnouncement = AllowSiteAnnouncement,
            AllowPasswordReset = AllowPasswordReset,
            AllowConfirmEmail = AllowConfirmEmail,
            AllowNotifications = AllowNotifications,
            AllowPushBuildStatus = AllowPushBuildStatus,
            AllowCommitBuildStatus = AllowCommitBuildStatus,
        };
    }

    public bool Allows(EmailReason reason)
    {
        if (DisableAllEmails)
            return false;

        return reason switch
        {
            EmailReason.SiteAnnouncement => AllowSiteAnnouncement,
            EmailReason.PasswordReset => AllowPasswordReset,
            EmailReason.ConfirmEmail => AllowConfirmEmail,
            EmailReason.Notifications => AllowNotifications,
            EmailReason.PushBuildStatus => AllowPushBuildStatus,
            EmailReason.CommitBuildStatus => AllowCommitBuildStatus,
            _ => true,
        };
    }
}
