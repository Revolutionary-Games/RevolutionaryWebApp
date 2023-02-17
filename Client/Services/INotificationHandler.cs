namespace ThriveDevCenter.Client.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ThriveDevCenter.Shared.Models.Enums;
using ThriveDevCenter.Shared.Notifications;

public interface INotificationHandler<in T> : IGroupListener
    where T : SerializedNotification
{
    public Task Handle(T notification, CancellationToken cancellationToken);
}

public interface IGroupListener
{
    /// <summary>
    ///   Gets the groups that this component wants to listen to. Groups are defined in NotificationGroups.cs
    /// </summary>
    /// <param name="currentAccessLevel">The current access there is to the system</param>
    /// <param name="groups">This is where the groups should be added</param>
    public void GetWantedListenedGroups(UserAccessLevel currentAccessLevel, ISet<string> groups);
}
