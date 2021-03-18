namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Models;
    using Shared;

    public abstract class BaseAuthenticationHelper : IMiddleware
    {
        protected enum AuthMethodResult
        {
            Authenticated,
            Nothing,
            Error,
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            // Skip if already authenticated
            // For some reason now when cookies are sent the Identity, by default is set to some unauthenticated one
            // TODO: could try to figure out what does that and try to disable it
            if (context.User.Identity == null || !context.Items.ContainsKey(AppInfo.CurrentUserMiddleWareKey))
            {
                if (!await PerformAuthentication(context))
                    return;
            }

            await next.Invoke(context);
        }

        /// <summary>
        ///   Run the actual authentication step
        /// </summary>
        /// <param name="context">Current running context</param>
        /// <returns>False on error (should have already wrote out the error response)</returns>
        protected abstract Task<bool> PerformAuthentication(HttpContext context);

        protected void OnAuthenticationSucceeded(HttpContext context, User user,
            AuthenticationScopeRestriction restriction)
        {
            if (user == null)
                throw new ArgumentException("can't set authenticated user to null");

            var identity = new ClaimsIdentity(user);

            context.User.AddIdentity(identity);
            context.Items[AppInfo.CurrentUserMiddleWareKey] = user;
            context.Items["AuthenticatedUserScopeRestriction"] = restriction;
        }
    }
}
