namespace ThriveDevCenter.Shared.Utilities;

using System;
using DevCenterCommunication.Models.Enums;
using Models;
using Models.Enums;

public static class FileAccessHelpers
{
    public static bool IsAccessibleTo(this FileAccess access, IUserGroupData? userGroups, long? userId,
        long? objectOwnerId)
    {
        // Everyone has access to public
        if (access == FileAccess.Public)
            return true;

        // Unauthenticated users can only view public items
        // False is returned here as public access was checked above
        if (userId == null || userGroups == null)
            return false;

        // Admins can access anything not system-only
        if (userGroups.HasAccessLevel(GroupType.Admin))
            return access != FileAccess.Nobody;

        // Object owner access
        if (objectOwnerId != null && userId == objectOwnerId)
            return access != FileAccess.Nobody;

        if (userGroups.HasAccessLevel(GroupType.Developer))
        {
            return access is FileAccess.User or FileAccess.RestrictedUser or FileAccess.Developer;
        }

        if (userGroups.HasAccessLevel(GroupType.RestrictedUser))
        {
            return access == FileAccess.RestrictedUser;
        }

        return access is FileAccess.User or FileAccess.RestrictedUser;
    }

    public static string ToUserReadableString(this FileAccess access)
    {
        switch (access)
        {
            case FileAccess.Public:
                return "public";
            case FileAccess.RestrictedUser:
                return "restricted users";
            case FileAccess.User:
                return "users";
            case FileAccess.Developer:
                return "developers";
            case FileAccess.OwnerOrAdmin:
                return "owner";
            case FileAccess.Nobody:
                return "system";
            default:
                throw new ArgumentOutOfRangeException(nameof(access), access, null);
        }
    }

    public static FileAccess AccessFromUserReadableString(string access)
    {
        switch (access.ToLowerInvariant())
        {
            case "public":
                return FileAccess.Public;
            case "restricted user":
            case "restricted users":
            case "restrictedUser":
            case "restrictedUsers":
            // ReSharper disable StringLiteralTypo
            case "restricteduser":
            case "restrictedusers":
            case "ruser":
            case "rusers":
                // ReSharper restore StringLiteralTypo
                return FileAccess.RestrictedUser;
            case "users":
            case "user":
                return FileAccess.User;
            case "developers":
            case "developer":
                return FileAccess.Developer;
            case "owner":
            case "owners":
            case "owner + admins":
            case "admins":
            case "admin":
                return FileAccess.OwnerOrAdmin;
            case "system":
            case "nobody":
                return FileAccess.Nobody;
            default:
                throw new ArgumentException("Unknown name for FileAccess");
        }
    }
}
