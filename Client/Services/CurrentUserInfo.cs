namespace ThriveDevCenter.Client.Services;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ThriveDevCenter.Shared.Models;
using ThriveDevCenter.Shared.Models.Enums;
using ThriveDevCenter.Shared.Notifications;

/// <summary>
///   Receives and holds current user info from the server. Currently not designed that the info changes later
/// </summary>
public class CurrentUserInfo : INotificationHandler<UserUpdated>
{
    private UserDTO? info;

    public delegate void UserInfoChangedEventHandler(object sender, UserDTO? newInfo);

    public event UserInfoChangedEventHandler? OnUserInfoChanged;

    /// <summary>
    ///   When InfoReady is true, this is non-null if currently logged in, and null if client is not logged in
    /// </summary>
    public UserDTO? Info
    {
        get
        {
            if (!InfoReady)
                throw new InvalidOperationException("Current User info is not ready yet");

            return info;
        }
        private set
        {
            // Don't compare just the key or something like that as any other property could change that we want
            // to detect
            if (info == value)
                return;

            info = value;

            OnUserInfoChanged?.Invoke(this, info);
        }
    }

    public bool LoggedIn => Info != null;

    /// <summary>
    ///   True once Info (and other derived properties are valid)
    /// </summary>
    public bool InfoReady { get; private set; }

    public bool IsAdmin => HasGroup(GroupType.Admin);
    public bool IsDeveloper => HasGroup(GroupType.Developer);
    public IUserGroupData? Groups => Info?.Groups;

    public string? Username => Info?.Name;
    public string? Email => Info?.Email;

    public bool HasGroup(GroupType groupType)
    {
        // Everyone has public access
        if (groupType == GroupType.NotLoggedIn)
            return true;

        return Groups?.HasGroup(groupType) == true;
    }

    /// <summary>
    ///   This is separate from groups as restricted or above level can't be checked otherwise
    /// </summary>
    /// <param name="accessLevel">Access to check for current user</param>
    /// <returns>True when has access</returns>
    public bool HasAccessLevel(GroupType accessLevel)
    {
        // Everyone has public access
        if (accessLevel == GroupType.NotLoggedIn)
            return true;

        return Groups?.HasAccessLevel(accessLevel) == true;
    }

    /// <summary>
    ///   Call whenever receiving user info objects from the server. Used to react to our own user data changing
    /// </summary>
    /// <param name="user">The user info we received</param>
    public void OnReceivedAnUsersInfo(UserDTO? user)
    {
        if (!InfoReady || user == null)
            return;

        if (info == null || info.Id != user.Id)
            return;

        OnReceivedOurInfo(user);
    }

    public void OnReceivedOurInfo(UserDTO? user)
    {
        var previousInfo = InfoReady;

        InfoReady = true;
        Info = user;

        // Null value doesn't trigger the initial callback, so we force that to happen here
        if (info == null && !previousInfo)
        {
            OnUserInfoChanged?.Invoke(this, info);
        }
    }

    public Task Handle(UserUpdated notification, CancellationToken cancellationToken)
    {
        OnReceivedAnUsersInfo(notification.Item);
        return Task.CompletedTask;
    }

    public void GetWantedListenedGroups(IUserGroupData currentUserGroups, ISet<string> groups)
    {
        if (!InfoReady)
            return;

        var fetchedInfo = Info;
        if (fetchedInfo == null)
            return;

        // Want to listen to our user updates
        var idStr = Convert.ToString(fetchedInfo.Id);

        groups.Add(NotificationGroups.UserUpdatedPrefix + idStr);

        // Admins can get more info about themselves
        if (IsAdmin)
        {
            groups.Add(NotificationGroups.UserUpdatedPrefixAdminInfo + idStr);
        }
    }
}
