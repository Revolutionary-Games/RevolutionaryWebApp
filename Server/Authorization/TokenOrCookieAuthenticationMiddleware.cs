namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Linq;
    using System.Security.Claims;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared;

    public class TokenOrCookieAuthenticationMiddleware : IMiddleware
    {
        private readonly ApplicationDbContext database;

        private enum AuthMethodResult
        {
            Authenticated,
            Nothing,
            Error,
        }

        public TokenOrCookieAuthenticationMiddleware(ApplicationDbContext database)
        {
            this.database = database;
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

        private async Task<bool> PerformAuthentication(HttpContext context)
        {
            // API token is allowed to be in "api_token" or "apiToken" query parameter
            var result = await CheckQueryString(context);
            if (result != AuthMethodResult.Nothing)
                return result != AuthMethodResult.Error;

            // Or in Authorization header
            result = await CheckAuthorizationHeader(context);
            if (result != AuthMethodResult.Nothing)
                return result != AuthMethodResult.Error;

            // Or in a cookie
            result = await CheckCookie(context);
            if (result != AuthMethodResult.Nothing)
                return result != AuthMethodResult.Error;

            return true;
        }

        private async Task<AuthMethodResult> CheckQueryString(HttpContext context)
        {
            bool foundToken = context.Request.Query.TryGetValue("api_token", out var queryToken) &&
                queryToken.Count > 0;

            if (!foundToken && context.Request.Query.TryGetValue("apiToken", out queryToken) && queryToken.Count > 0)
                foundToken = true;

            if (foundToken && !string.IsNullOrEmpty(queryToken[0]))
            {
                var user = database.Users.FirstOrDefault(u => u.ApiToken == queryToken[0]);

                if (user != null)
                {
                    OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None);
                    return AuthMethodResult.Authenticated;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid token");

                return AuthMethodResult.Error;
            }

            return AuthMethodResult.Nothing;
        }

                private async Task<AuthMethodResult> CheckAuthorizationHeader(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue("Authorization", out StringValues header) && header.Count > 0)
            {
                // In format "Bearer TOKEN"
                // TODO: split off a middleware for authentication in the lfs endpoint that uses different token lookup
                // Or maybe it would make more sense to detect just the path in here to make things a bit simpler overall
                var tokenValue = header[0];

                if (tokenValue.StartsWith("Bearer "))
                {
                    return await CheckBearerToken(context, tokenValue);
                }
                else
                {
                    return await CheckLauncherLink(context, tokenValue);
                }
            }

            return AuthMethodResult.Nothing;
        }

        private async Task<AuthMethodResult> CheckBearerToken(HttpContext context, string tokenValue)
        {
            var apiToken = tokenValue.Split(' ').LastOrDefault();

            if (string.IsNullOrEmpty(apiToken))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("Authorization header format is invalid.");
                return AuthMethodResult.Error;
            }

            var user = database.Users.FirstOrDefault(u => u.ApiToken == apiToken);

            if (user != null)
            {
                OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None);
                return AuthMethodResult.Authenticated;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Invalid token");
            return AuthMethodResult.Error;
        }

        private async Task<AuthMethodResult> CheckLauncherLink(HttpContext context, string tokenValue)
        {
            // TODO: should maybe move the launcher to use a more standard format
            // Or just a plain which is an active launcher link

            var link = database.LauncherLinks.Include(l => l.User)
                .FirstOrDefault(l => l.LinkCode == tokenValue);

            if (link?.User == null)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid token");
                return AuthMethodResult.Error;
            }

            // TODO: update last ip
            link.TotalApiCalls += 1;

            // TODO: maybe run this part in a task
            await database.SaveChangesAsync();

            OnAuthenticationSucceeded(context, link.User, AuthenticationScopeRestriction.LauncherOnly);
            return AuthMethodResult.Authenticated;
        }

        private async Task<AuthMethodResult> CheckCookie(HttpContext context)
        {
            if (context.Request.Cookies.TryGetValue(AppInfo.SessionCookieName, out string session) &&
                !string.IsNullOrEmpty(session))
            {
                // TODO: sessions
                bool validSession = false;

                if (validSession)
                {
                    // When inside a cookie CSRF needs to have passed
                    if (!context.Items.TryGetValue("CSRF", out object csrf) && !(csrf is bool))
                    {
                        // TODO: the download endpoint shouldn't require this, or do we need a separate interactive
                        // TODO: page to offer the proper download button for downloads?
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsync("CSRF token is required when using cookies.");
                        return AuthMethodResult.Error;
                    }

                    // TODO: lookup user
                    User user = null;

                    if (user != null)
                    {
                        OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None);
                        return AuthMethodResult.Authenticated;
                    }

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Invalid session cookie");
                    return AuthMethodResult.Error;
                }
            }

            return AuthMethodResult.Nothing;
        }

        private void OnAuthenticationSucceeded(HttpContext context, User user,
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
