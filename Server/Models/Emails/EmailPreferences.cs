namespace RevolutionaryWebApp.Server.Models.Emails;

using Interfaces;
using Models;
using RevolutionaryWebApp.Shared.Models;
using Utilities;

/// <summary>
///   Abstract base class for email preference sets. Concrete implementations are stored in the DB.
/// </summary>
public abstract class EmailPreferences : UpdateableModel, IDTOCreator<EmailPreferencesDTO>
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
}
