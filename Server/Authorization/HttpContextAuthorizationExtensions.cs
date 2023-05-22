namespace ThriveDevCenter.Server.Authorization;

using System;
using Microsoft.AspNetCore.Http;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;

public static class HttpContextAuthorizationExtensions
{
    public enum AuthenticationResult
    {
        NoUser,
        NoAccess,
        Success,
    }

    /// <summary>
    ///   Variant that returns also information if user login details were not provided at all
    /// </summary>
    public static AuthenticationResult HasAuthenticatedUserWithGroupExtended(this HttpContext context,
        GroupType requiredGroup, AuthenticationScopeRestriction? requiredRestriction)
    {
        // Non-logged in is always allowed (even if scope restrictions don't match as in that case the user could
        // just not authenticate at all to have access, so preventing that seems a bit silly)
        if (requiredGroup == GroupType.NotLoggedIn)
            return AuthenticationResult.Success;

        var user = context.AuthenticatedUser();

        if (user == null)
            return AuthenticationResult.NoUser;

        if (!user.AccessCachedGroupsOrThrow().HasGroup(requiredGroup))
            return AuthenticationResult.NoAccess;

        if (requiredRestriction != null)
        {
            if (context.AuthenticatedUserRestriction() != requiredRestriction)
            {
                return AuthenticationResult.NoAccess;
            }
        }

        return AuthenticationResult.Success;
    }

    /// <summary>
    ///   Checks for current user access level. This exists to make checking for restricted level or above work, as
    ///   no higher users can actually be in the restricted group.
    /// </summary>
    public static AuthenticationResult HasAuthenticatedUserWithAccessLevelExtended(this HttpContext context,
        GroupType requiredAccess, AuthenticationScopeRestriction? requiredRestriction)
    {
        if (requiredAccess == GroupType.NotLoggedIn)
            return AuthenticationResult.Success;

        var user = context.AuthenticatedUser();

        if (user == null)
            return AuthenticationResult.NoUser;

        if (!user.AccessCachedGroupsOrThrow().HasAccessLevel(requiredAccess))
            return AuthenticationResult.NoAccess;

        if (requiredRestriction != null)
        {
            if (context.AuthenticatedUserRestriction() != requiredRestriction)
            {
                return AuthenticationResult.NoAccess;
            }
        }

        return AuthenticationResult.Success;
    }

    public static bool HasAuthenticatedUserWithGroup(this HttpContext context, GroupType requiredGroup,
        AuthenticationScopeRestriction? requiredRestriction)
    {
        return context.HasAuthenticatedUserWithGroupExtended(requiredGroup, requiredRestriction) ==
            AuthenticationResult.Success;
    }

    /// <summary>
    ///   Variant that returns also information if any key was provided
    /// </summary>
    public static AuthenticationResult HasAuthenticatedAccessKeyExtended(this HttpContext context,
        AccessKeyType requiredAccess)
    {
        var key = context.AuthenticatedAccessKey();

        if (key == null)
            return AuthenticationResult.NoUser;

        if (key.KeyType != requiredAccess)
            return AuthenticationResult.NoAccess;

        return AuthenticationResult.Success;
    }

    public static bool HasAuthenticatedAccessKey(this HttpContext context, AccessKeyType requiredAccess)
    {
        return context.HasAuthenticatedAccessKeyExtended(requiredAccess) == AuthenticationResult.Success;
    }

    /// <summary>
    ///   Gets the scope restriction of currently active user
    /// </summary>
    /// <param name="context">The http context</param>
    /// <returns>The restriction applying to the current user based on how they authenticated</returns>
    /// <exception cref="InvalidOperationException">
    ///   If there is none set. NOTE: an user must be set, so if you don't want to handle this exception first get
    ///   the authenticated user and check that the user is not null.
    /// </exception>
    public static AuthenticationScopeRestriction AuthenticatedUserRestriction(this HttpContext context)
    {
        if (!context.Items.TryGetValue(AppInfo.AuthenticationScopeRestrictionMiddleWareKey,
                out object? restrictionRaw) || restrictionRaw == null)
            throw new InvalidOperationException("authentication scope restriction was not set");

        return (AuthenticationScopeRestriction)restrictionRaw;
    }

    public static User? AuthenticatedUser(this HttpContext context)
    {
        if (context.User.Identity == null ||
            !context.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object? rawUser))
        {
            return null;
        }

        return rawUser as User;
    }

    public static User AuthenticatedUserOrThrow(this HttpContext context)
    {
        if (context.User.Identity == null ||
            !context.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object? rawUser) || rawUser == null)
        {
            throw new InvalidOperationException(
                "No authenticated user when requested. Should have been checked " +
                $"before or {nameof(AuthenticatedUser)} should be used");
        }

        return (User)rawUser;
    }

    public static Session? AuthenticatedUserSession(this HttpContext context)
    {
        if (context.User.Identity == null ||
            !context.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object? rawUser) || rawUser is null)
        {
            return null;
        }

        if (!context.Items.TryGetValue(AppInfo.CurrentUserSessionMiddleWareKey, out object? rawSession))
            return null;

        return rawSession as Session;
    }

    public static (User? User, AuthenticationScopeRestriction Restriction) AuthenticatedUserWithRestriction(
        this HttpContext context)
    {
        if (context.User.Identity == null ||
            !context.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object? rawUser))
        {
            return (null, AuthenticationScopeRestriction.None);
        }

        return (rawUser as User, context.AuthenticatedUserRestriction());
    }

    public static AccessKey? AuthenticatedAccessKey(this HttpContext context)
    {
        if (!context.Items.TryGetValue(AppInfo.AccessKeyMiddlewareKey, out object? raw))
        {
            return null;
        }

        return raw as AccessKey;
    }

    public static LauncherLink? UsedLauncherLink(this HttpContext context)
    {
        if (!context.Items.TryGetValue(AppInfo.LauncherLinkMiddlewareKey, out object? raw))
        {
            return null;
        }

        return raw as LauncherLink;
    }
}
