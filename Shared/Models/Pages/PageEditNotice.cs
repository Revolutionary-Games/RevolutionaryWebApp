namespace RevolutionaryWebApp.Shared.Models.Pages;

using Notifications;

public class PageEditNotice : SerializedNotification
{
    /// <summary>
    ///   ID of the edited page
    /// </summary>
    public long PageId { get; set; }

    /// <summary>
    ///   ID of the user doing the editing
    /// </summary>
    public long EditorUserId { get; set; }

    /// <summary>
    ///   True when the edit was just saved
    /// </summary>
    public bool Saved { get; set; }
}
