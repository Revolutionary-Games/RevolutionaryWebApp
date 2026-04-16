namespace RevolutionaryWebApp.Shared.Models.Enums;

/// <summary>
///   Reasons/categories for sending emails from the system.
/// </summary>
public enum EmailReason
{
    Notifications,
    SiteAnnouncement,
    PasswordReset,
    ConfirmEmail,
    PushBuildStatus,
    CommitBuildStatus,

    /// <summary>
    ///   Important emails, used for very important things. Cannot be turned off without turning off *all* emails.
    /// </summary>
    ImportantEmails,
}
