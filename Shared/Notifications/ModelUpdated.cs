namespace ThriveDevCenter.Shared.Notifications
{
    using Models;

    /// <summary>
    ///   Notification about a single model page information being updated
    /// </summary>
    public abstract class ModelUpdated<T> : SerializedNotification
        where T : class
    {
        public T Item { get; init; }
    }

    public class UserUpdated : ModelUpdated<UserInfo>
    {
    }
}
