namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    public class TokenOrCookieAuthenticationMiddleware : BaseAuthenticationHelper
    {
        private readonly ApplicationDbContext database;

        public TokenOrCookieAuthenticationMiddleware(ApplicationDbContext database)
        {
            this.database = database;
        }

        protected override async Task<bool> PerformAuthentication(HttpContext context)
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
                var user = await database.Users.WhereHashed(nameof(User.ApiToken), queryToken[0])
                    .Include(u => u.AssociationMember).AsAsyncEnumerable()
                    .FirstOrDefaultAsync(u => u.ApiToken == queryToken[0]);

                if (user != null && user.Suspended != true)
                {
                    OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None, null);
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
                var tokenValue = header[0];

                if (tokenValue.StartsWith("Bearer "))
                {
                    // In format "Bearer TOKEN"
                    return await CheckBearerToken(context, tokenValue);
                }

                if (!tokenValue.Contains(' '))
                {
                    // In another format (only check launcher link if no spaces, as that might be basic authentication
                    // (handled separately in the LFS authentication middleware)
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

            var user = await database.Users.WhereHashed(nameof(User.ApiToken), apiToken).Include(u => u.AssociationMember).AsAsyncEnumerable()
                .FirstOrDefaultAsync(u => u.ApiToken == apiToken);

            if (user != null && user.Suspended != true)
            {
                OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None, null);
                return AuthMethodResult.Authenticated;
            }

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Invalid token");
            return AuthMethodResult.Error;
        }

        private async Task<AuthMethodResult> CheckLauncherLink(HttpContext context, string tokenValue)
        {
            // TODO: should maybe move the launcher to use a more standard format

            var link = await database.LauncherLinks.WhereHashed(nameof(LauncherLink.LinkCode), tokenValue)
                .Include(l => l.User)
                .FirstOrDefaultAsync(l => l.LinkCode == tokenValue);

            if (link?.User == null || link.User.Suspended == true)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(new BasicJSONErrorResult("Invalid token", "Access token is invalid")
                    .ToString());
                return AuthMethodResult.Error;
            }

            // TODO: this should probably be removed? or not used just here
            if (context.Connection.RemoteIpAddress == null)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    new BasicJSONErrorResult("Internal server error",
                            "Internal server error when getting remote address")
                        .ToString());
                return AuthMethodResult.Error;
            }

            // As total API calls is updated anyway, the last connection and last IP are updated at the same time
            link.LastConnection = DateTime.UtcNow;
            link.LastIp = context.Connection.RemoteIpAddress.ToString();
            link.TotalApiCalls += 1;

            // TODO: maybe run this part in a task
            await database.SaveChangesAsync();

            context.Items[AppInfo.LauncherLinkMiddlewareKey] = link;

            OnAuthenticationSucceeded(context, link.User, AuthenticationScopeRestriction.LauncherOnly, null);
            return AuthMethodResult.Authenticated;
        }

        private async Task<AuthMethodResult> CheckCookie(HttpContext context)
        {
            if (context.Request.Cookies.TryGetValue(AppInfo.SessionCookieName, out var session) &&
                !string.IsNullOrEmpty(session))
            {
                var (user, sessionObject) = await context.Request.Cookies.GetUserFromSession(database,
                    context.Connection.RemoteIpAddress);

                if (user != null)
                {
                    // When inside a cookie CSRF needs to have passed
                    // TODO: the download endpoint shouldn't require this, or do we need a separate interactive
                    // TODO: page to offer the proper download button for downloads?

                    context.Items[AppInfo.CSRFNeededName] = true;

                    if (user.Suspended != true)
                    {
                        OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None, sessionObject);
                        return AuthMethodResult.Authenticated;
                    }

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Invalid session cookie");
                    return AuthMethodResult.Error;
                }
            }

            return AuthMethodResult.Nothing;
        }
    }
}
