namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Models;

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
            if (context.User.Identity == null)
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

            context.User.AddIdentity(new ClaimsIdentity(user));
            context.Items["AuthenticatedUser"] = user;
            context.Items["AuthenticatedUserScopeRestriction"] = restriction;
        }
    }
}
