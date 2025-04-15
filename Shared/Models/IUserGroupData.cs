namespace RevolutionaryWebApp.Shared.Models;

using System;
using System.Collections.Generic;
using Enums;

public interface IUserGroupData
{
    public IEnumerable<GroupType> Groups { get; }
}

public static class UserGroupDataExtensions
{
    public static bool IsUser(this IUserGroupData currentGroups)
    {
        foreach (var group in currentGroups.Groups)
        {
            if (group is GroupType.NotLoggedIn)
                continue;

            return true;
        }

        return false;
    }

    public static bool IsDeveloper(this IUserGroupData currentGroups)
    {
        foreach (var group in currentGroups.Groups)
        {
            if (group is GroupType.Developer or GroupType.Admin)
                return true;
        }

        return false;
    }

    /// <summary>
    ///   Checks user has access to any type of group. Has special handling for things <see cref="HasAccessLevel"/>
    ///   can handle.
    /// </summary>
    /// <param name="currentGroups">The current user's groups</param>
    /// <param name="requiredGroup">What group the user needs</param>
    /// <returns>True if user has the required group</returns>
    /// <remarks>
    ///   <para>
    ///     Note that for non-basic groups (access level compatible) admins are not automatically assumed to posses
    ///     that group
    ///   </para>
    /// </remarks>
    public static bool HasGroup(this IUserGroupData currentGroups, GroupType requiredGroup)
    {
        switch (requiredGroup)
        {
            case GroupType.NotLoggedIn:

            // Restricted user cannot be checked here as the access level is granted to everyone above it
            // case GroupType.RestrictedUser:
            case GroupType.User:
            case GroupType.Developer:
            case GroupType.Admin:
            case GroupType.SystemOnly:
                return HasAccessLevel(currentGroups, requiredGroup);
        }

        foreach (var group in currentGroups.Groups)
        {
            if (group == requiredGroup)
                return true;
        }

        return false;
    }

    /// <summary>
    ///   Checks certain access level is in groups. Only certain group memberships can be checked like this
    /// </summary>
    /// <param name="currentAccess">The groups that the user has</param>
    /// <param name="requiredAccess">
    ///   What the user should have (only admin, logged in, restricted, developer are allowed)
    /// </param>
    /// <returns>True when has enough access</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///   If the <see cref="requiredAccess"/> is not an allowed value
    /// </exception>
    public static bool HasAccessLevel(this IUserGroupData currentAccess, GroupType requiredAccess)
    {
        // All possible groups and not logged-in users are this access level
        if (requiredAccess == GroupType.NotLoggedIn)
            return true;

        if (requiredAccess == GroupType.SystemOnly)
            return false;

        // Only some specific groups are allowed to be checked
        switch (requiredAccess)
        {
            case GroupType.Admin:
            case GroupType.Developer:
            case GroupType.User:
            case GroupType.RestrictedUser:
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(requiredAccess), "Disallowed group type for this method");
        }

        foreach (var group in currentAccess.Groups)
        {
            if (group >= requiredAccess)
            {
                // Check if the group is one of the few that allow access
                switch (group)
                {
                    case GroupType.Admin:
                    case GroupType.Developer:
                    case GroupType.User:
                    case GroupType.RestrictedUser:
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    ///   Primary group of an user, only really useful for display in space constrained places
    /// </summary>
    /// <param name="currentGroups">The current groups</param>
    /// <returns>The primary group</returns>
    public static GroupType ComputePrimaryGroup(this IUserGroupData currentGroups)
    {
        if (HasAccessLevel(currentGroups, GroupType.Admin))
            return GroupType.Admin;

        if (HasAccessLevel(currentGroups, GroupType.Developer))
            return GroupType.Developer;

        if (HasAccessLevel(currentGroups, GroupType.User))
            return GroupType.User;

        if (HasAccessLevel(currentGroups, GroupType.RestrictedUser))
            return GroupType.RestrictedUser;

        return GroupType.NotLoggedIn;
    }
}
