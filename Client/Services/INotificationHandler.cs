namespace ThriveDevCenter.Client.Services;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ThriveDevCenter.Shared.Models;
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
    /// <param name="currentUserGroups">
    ///   The current groups the user accessing the system has. When not logged in this is initialized with just the
    ///   <see cref="GroupType.NotLoggedIn"/> group.
    /// </param>
    /// <param name="groups">This is where the groups to listen to should be added</param>
    public void GetWantedListenedGroups(IUserGroupData currentUserGroups, ISet<string> groups);
}
