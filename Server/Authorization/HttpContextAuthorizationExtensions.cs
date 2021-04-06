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
                if (!context.Items.TryGetValue("AuthenticatedUserScopeRestriction",
                    out object restrictionRaw))
                    throw new InvalidOperationException("authentication scope restriction was not set");

                var restriction = (AuthenticationScopeRestriction)restrictionRaw;

                if (restriction != requiredRestriction)
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

        public static User AuthenticatedUser(this HttpContext context)
        {
            if (context.User.Identity == null ||
                !context.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object rawUser))
            {
                return null;
            }

            return rawUser as User;
        }
    }
}
