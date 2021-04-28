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

    // These separate class types are needed for JSON serialization to work

    public class UserUpdated : ModelUpdated<UserInfo>
    {
    }

    public class LFSProjectUpdated : ModelUpdated<LFSProjectDTO>
    {
    }

    public class DevBuildUpdated : ModelUpdated<DevBuildDTO>
    {
    }
}
