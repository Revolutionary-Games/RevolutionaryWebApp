namespace RevolutionaryWebApp.Server.Authorization;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Models;
using Services;
using Shared;
using Shared.Models;
using Utilities;

public class TokenOrCookieAuthenticationMiddleware : BaseAuthenticationHelper
{
    private readonly ILogger<TokenOrCookieAuthenticationMiddleware> logger;
    private readonly ApplicationDbContext database;

    public TokenOrCookieAuthenticationMiddleware(ILogger<TokenOrCookieAuthenticationMiddleware> logger,
        ApplicationDbContext database, CustomMemoryCache memoryCache) : base(memoryCache)
    {
        this.logger = logger;
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
            if (queryToken[0]!.Length > 100)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid token (too long)");

                return AuthMethodResult.Error;
            }

            var cacheKey = "api:" + queryToken[0];

            if (IsNegativeAuthenticationAttemptCached(cacheKey))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid token");

                return AuthMethodResult.Error;
            }

            var user = await database.Users.WhereHashed(nameof(User.ApiToken), queryToken[0]!)
                .Include(u => u.AssociationMember).AsAsyncEnumerable()
                .FirstOrDefaultAsync(u => u.ApiToken == queryToken[0]);

            if (user != null && user.Suspended != true)
            {
                // When using tokens we can't cache the groups list in the session object so we need to load them here
                await user.ComputeUserGroups(database);

                OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None, null);
                return AuthMethodResult.Authenticated;
            }

            RememberFailedAuthentication(cacheKey);

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

            if (string.IsNullOrEmpty(tokenValue))
                return AuthMethodResult.Nothing;

            if (tokenValue.StartsWith("Bearer "))
            {
                // In format "Bearer TOKEN"
                return await CheckBearerToken(context, tokenValue);
            }

            if (!tokenValue.Contains(' ') && tokenValue.Length < AppInfo.MaxTokenLength)
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

        if (string.IsNullOrEmpty(apiToken) || apiToken.Length > AppInfo.MaxTokenLength)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Authorization header format is invalid.");
            return AuthMethodResult.Error;
        }

        var cacheKey = "api:" + apiToken;

        if (IsNegativeAuthenticationAttemptCached(cacheKey))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Invalid token");
            return AuthMethodResult.Error;
        }

        var user = await database.Users.WhereHashed(nameof(User.ApiToken), apiToken).Include(u => u.AssociationMember)
            .AsAsyncEnumerable().FirstOrDefaultAsync(u => u.ApiToken == apiToken);

        if (user != null && user.Suspended != true)
        {
            // When using tokens we can't cache the groups list in the session object so we need to load them here
            await user.ComputeUserGroups(database);

            OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None, null);
            return AuthMethodResult.Authenticated;
        }

        RememberFailedAuthentication(cacheKey);

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Invalid token");
        return AuthMethodResult.Error;
    }

    private async Task<AuthMethodResult> CheckLauncherLink(HttpContext context, string tokenValue)
    {
        // TODO: should maybe move the launcher to use a more standard format for tokens

        var cacheKey = "launcher:" + tokenValue;

        if (IsNegativeAuthenticationAttemptCached(cacheKey))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(new BasicJSONErrorResult("Invalid token", "Access token is invalid")
                .ToString());
            return AuthMethodResult.Error;
        }

        var link = await database.LauncherLinks.WhereHashed(nameof(LauncherLink.LinkCode), tokenValue)
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.LinkCode == tokenValue);

        if (link?.User == null || link.User.Suspended == true)
        {
            RememberFailedAuthentication(cacheKey);

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

        var groups = link.CachedUserGroups;

        if (groups == null)
        {
            // Recompute groups here as the link model allows groups to be null (which is different from the main
            // sessions where the opposite design is used)
            await link.User.ComputeUserGroups(database);
            link.CachedUserGroups = link.User.AccessCachedGroupsOrThrow();
        }
        else
        {
            link.User.SetGroupsFromLauncherLinkCache(groups);
        }

        // TODO: maybe run this part in a task (to block the authentication for less time)
        await database.SaveChangesAsync();

        context.Items[AppInfo.LauncherLinkMiddlewareKey] = link;

        OnAuthenticationSucceeded(context, link.User, AuthenticationScopeRestriction.LauncherOnly, null);
        return AuthMethodResult.Authenticated;
    }

    private async Task<AuthMethodResult> CheckCookie(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(AppInfo.SessionCookieName, out var sessionRaw) &&
            !string.IsNullOrEmpty(sessionRaw))
        {
            if (sessionRaw.Length > AppInfo.MaxTokenLength)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid session cookie (too long)");
                return AuthMethodResult.Error;
            }

            // Roughly check the cookie format here
            int index = sessionRaw.IndexOf(':');
            if (index == -1)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid session cookie (malformed)");
                return AuthMethodResult.Error;
            }

            var cacheKey = "sessionKey:" + sessionRaw;

            // Don't load DB data if we remember this data being bad
            if (IsNegativeAuthenticationAttemptCached(cacheKey))
            {
                return AuthMethodResult.Nothing;
            }

            var (user, sessionObject) = await context.Request.Cookies.GetUserFromSession(database,
                context.Connection.RemoteIpAddress);

            if (user != null)
            {
                if (sessionObject == null)
                    throw new InvalidOperationException("User was found but no session exists");

                // When inside a cookie CSRF needs to have passed (except for some download endpoints)
                context.Items[AppInfo.CSRFNeededName] = true;

                if (user.Suspended != true)
                {
                    // Ensure groups in session are up to date, if not up to date update them
                    if (sessionObject.CachedUserGroups == null)
                    {
                        // Database data problem, UpdateUserGroupCacheJob should always keep the groups in sessions up
                        // to date
                        logger.LogError("Database session data is incorrect for user: {UserId}, session: {Session}",
                            user.Id, sessionObject.Id);
                        return AuthMethodResult.Nothing;
                    }

                    // Can use session cached groups
                    user.SetGroupsFromSessionCache(sessionObject);

                    OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None, sessionObject);
                    return AuthMethodResult.Authenticated;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid session cookie");
                return AuthMethodResult.Error;
            }

            RememberFailedAuthentication(cacheKey);
        }

        return AuthMethodResult.Nothing;
    }
}
