namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using Microsoft.AspNetCore.Http;
    using Models;
    using Shared;
    using Shared.Models;

    public static class HttpContextAuthorizationExtensions
    {
        public enum AuthenticationResult
        {
            NoUser,
            NoAccess,
            Success
        }

        /// <summary>
        ///   Variant that returns also information if user login details were not provided at all
        /// </summary>
        public static AuthenticationResult HasAuthenticatedUserWithAccessExtended(this HttpContext context,
            UserAccessLevel requiredAccess,
            AuthenticationScopeRestriction? requiredRestriction)
        {
            // Non-logged in is always allowed (even if scope restrictions don't match as in that case the user could
            // just not authenticate at all to have access, so preventing that seems a bit silly)
            if (requiredAccess == UserAccessLevel.NotLoggedIn)
                return AuthenticationResult.Success;

            var user = context.AuthenticatedUser();

            if (user == null)
                return AuthenticationResult.NoUser;

            if (!user.HasAccessLevel(requiredAccess))
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

        public static bool HasAuthenticatedUserWithAccess(this HttpContext context, UserAccessLevel requiredAccess,
            AuthenticationScopeRestriction? requiredRestriction)
        {
            return context.HasAuthenticatedUserWithAccessExtended(requiredAccess, requiredRestriction) ==
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
            if (!context.Items.TryGetValue("AuthenticatedUserScopeRestriction",
                out object restrictionRaw))
                throw new InvalidOperationException("authentication scope restriction was not set");

            return (AuthenticationScopeRestriction)restrictionRaw;
        }

        public static User AuthenticatedUser(this HttpContext context)
        {
            if (context.User.Identity == null ||
                !context.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object rawUser))
            {
                return null;
            }

            return rawUser as User;
        }

        public static (User user, AuthenticationScopeRestriction restriction) AuthenticatedUserWithRestriction(
            this HttpContext context)
        {
            if (context.User.Identity == null ||
                !context.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object rawUser))
            {
                return (null, AuthenticationScopeRestriction.None);
            }

            return (rawUser as User, context.AuthenticatedUserRestriction());
        }

        public static AccessKey AuthenticatedAccessKey(this HttpContext context)
        {
            if (!context.Items.TryGetValue(AppInfo.AccessKeyMiddlewareKey, out object raw))
            {
                return null;
            }

            return raw as AccessKey;
        }
    }
}
