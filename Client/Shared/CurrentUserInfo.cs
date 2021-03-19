namespace ThriveDevCenter.Client.Shared
{
    using System;
    using ThriveDevCenter.Shared;
    using ThriveDevCenter.Shared.Models;

    /// <summary>
    ///   Receives and holds current user info from the server. Currently not designed that the info changes later
    /// </summary>
    public class CurrentUserInfo
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

        public UserAccessLevel AccessLevel => Info.AccessLevel;

        public string Username => Info.Name;
        public string Email => Info.Email;

        /// <summary>
        ///   Call whenever receiving user info objects from the server. Used to react to our own user data changing
        /// </summary>
        /// <param name="user">The user info we received</param>
        public void OnReceivedAnUsersInfo(UserInfo user)
        {
            if (!InfoReady)
                return;

            if (info == null || info.Id != user.Id)
                return;

            Info = user;
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
    }
}
