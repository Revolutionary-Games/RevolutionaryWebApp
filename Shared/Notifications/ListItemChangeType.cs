namespace RevolutionaryWebApp.Shared.Notifications;

/// <summary>
///   Specifies what happened in a list change notification
/// </summary>
public enum ListItemChangeType
{
    /// <summary>
    ///   An item's properties were updated
    /// </summary>
    ItemUpdated,

    /// <summary>
    ///   An item was deleted
    /// </summary>
    ItemDeleted,

    /// <summary>
    ///   A new item was added
    /// </summary>
    ItemAdded,
}
