namespace ThriveDevCenter.Client.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using ThriveDevCenter.Shared.Models;
    using ThriveDevCenter.Shared.Notifications;

    /// <summary>
    ///   Receives and holds current user info from the server. Currently not designed that the info changes later
    /// </summary>
    public class CurrentUserInfo : INotificationHandler<UserUpdated>, INotificationHandler<UserListUpdated>
    {
        private UserInfo info;

        public delegate void UserInfoChangedEventHandler(object sender, UserInfo newInfo);

        public event UserInfoChangedEventHandler OnUserInfoChanged;

        /// <summary>
        ///   When InfoReady is true, this is non-null if currently logged in, and null if client is not logged in
        /// </summary>
        public UserInfo Info
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

        public bool IsAdmin => Info?.Admin ?? false;
        public bool IsDeveloper => Info?.Developer ?? false;
        public bool IsUser => LoggedIn;

        public UserAccessLevel AccessLevel => Info?.AccessLevel ?? UserAccessLevel.NotLoggedIn;

        public string Username => Info.Name;
        public string Email => Info.Email;

        /// <summary>
        ///   Call whenever receiving user info objects from the server. Used to react to our own user data changing
        /// </summary>
        /// <param name="user">The user info we received</param>
        public void OnReceivedAnUsersInfo(UserInfo user)
        {
            if (!InfoReady || user == null)
                return;

            if (info == null || info.Id != user.Id)
                return;

            OnReceivedOurInfo(user);
        }

        public void OnReceivedOurInfo(UserInfo user)
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

        public Task Handle(UserListUpdated notification, CancellationToken cancellationToken)
        {
            OnReceivedAnUsersInfo(notification.Item);
            return Task.CompletedTask;
        }

        public void GetWantedListenedGroups(UserAccessLevel currentAccessLevel, ISet<string> groups)
        {
            if (!InfoReady || info == null)
                return;

            // Want to listen to our user updates
            var idStr = Convert.ToString(Info.Id);

            groups.Add(NotificationGroups.UserUpdatedPrefix + idStr);

            // Admins can get more info about themselves
            if (IsAdmin)
            {
                groups.Add(NotificationGroups.UserUpdatedPrefixAdminInfo + idStr);
            }
        }
    }
}
