namespace RevolutionaryWebApp.Server.Models.Emails;

using Models;

/// <summary>
///   Abstract base class for email preference sets. Concrete implementations are stored in the DB.
/// </summary>
public abstract class EmailPreferences : UpdateableModel
{
    /// <summary>
    ///   When true, no emails are sent regardless of individual category settings.
    /// </summary>
    public bool DisableAllEmails { get; set; } = false;

    // Per-category allow flags (default to true)
    public bool AllowSiteAnnouncement { get; set; } = true;
    public bool AllowPasswordReset { get; set; } = true;
    public bool AllowConfirmEmail { get; set; } = true;
    public bool AllowNotifications { get; set; } = true;
    public bool AllowPushBuildStatus { get; set; } = true;
    public bool AllowCommitBuildStatus { get; set; } = true;
}
