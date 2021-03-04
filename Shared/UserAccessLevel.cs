namespace ThriveDevCenter.Shared
{
    /// <summary>
    ///   The level of access a given user (or no user) has to the system
    /// </summary>
    public enum UserAccessLevel
    {
        NotLoggedIn,
        User,
        Developer,
        Admin
    }

    public static class UserAccessLevelHelpers
    {
        public static bool IsUser(this UserAccessLevel level)
        {
            switch (level)
            {
                case UserAccessLevel.User:
                case UserAccessLevel.Admin:
                case UserAccessLevel.Developer:
                    return true;
            }

            return false;
        }

        public static bool IsDeveloper(this UserAccessLevel level)
        {
            switch (level)
            {
                case UserAccessLevel.Admin:
                case UserAccessLevel.Developer:
                    return true;
            }

            return false;
        }
    }
}
