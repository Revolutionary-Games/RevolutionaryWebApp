namespace ThriveDevCenter.Shared.Models.Enums;

using System;

public static class UserAccessLevelExtensions
{
    public static bool IsUser(this UserAccessLevel level)
    {
        switch (level)
        {
            case UserAccessLevel.RestrictedUser:
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

    public static bool HasAccess(this UserAccessLevel currentAccess, UserAccessLevel requiredAccess)
    {
        if (currentAccess == requiredAccess)
            return true;

        switch (requiredAccess)
        {
            case UserAccessLevel.NotLoggedIn:
                // All possible currentAccess values are acceptable here
                return true;
            case UserAccessLevel.RestrictedUser:
                return currentAccess != UserAccessLevel.NotLoggedIn;
            case UserAccessLevel.User:
                return currentAccess != UserAccessLevel.NotLoggedIn &&
                    currentAccess != UserAccessLevel.RestrictedUser;
            case UserAccessLevel.Developer:
                return currentAccess is UserAccessLevel.Developer or UserAccessLevel.Admin;
            case UserAccessLevel.Admin:
                return currentAccess == UserAccessLevel.Admin;
            default:
                throw new ArgumentOutOfRangeException(nameof(requiredAccess), requiredAccess, null);
        }
    }
}
