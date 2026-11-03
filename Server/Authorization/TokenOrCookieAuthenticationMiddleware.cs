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
using Shared.Models.Enums;
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
        var result = await CheckAuthorizationHeader(context);
        if (result != AuthMethodResult.Nothing)
            return result != AuthMethodResult.Error;

        // Or in a cookie
        result = await CheckCookie(context);
        if (result != AuthMethodResult.Nothing)
            return result != AuthMethodResult.Error;

        return true;
    }

    private async Task<AuthMethodResult> CheckAuthorizationHeader(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("Authorization", out StringValues header) && header.Count > 0)
        {
            var tokenValue = header[0];

            if (string.IsNullOrEmpty(tokenValue))
                return AuthMethodResult.Nothing;

            if (tokenValue.StartsWith("key:", StringComparison.Ordinal))
            {
                // In format "key:ID:SECRET"
                return await CheckAPIKey(context, tokenValue);
            }

            if (!tokenValue.Contains(' ') && tokenValue.Length < AppInfo.MaxTokenLength)
            {
                // In another format (only check the launcher link if no spaces, as that might be basic authentication
                // (handled separately in the LFS authentication middleware)
                return await CheckLauncherLink(context, tokenValue);
            }
        }

        return AuthMethodResult.Nothing;
    }

    private async Task<AuthMethodResult> CheckAPIKey(HttpContext context, string tokenValue)
    {
        var parts = tokenValue.Split(':');
        APIKey? key = null;
        if (parts.Length == 3 && parts[1].Length == 14 && parts[2].Length == 36)
        {
            key = await database.APIKeys.WhereHashed(nameof(APIKey.Id), parts[1])
                .Include(k => k.User).ThenInclude(u => u.AssociationMember).AsAsyncEnumerable()
                .FirstOrDefaultAsync(k => SecurityHelpers.SlowEquals(k.Id, parts[1]));
        }

        if (key == null || !SecurityHelpers.SlowEquals(key.Secret, parts[2]) ||
            key.User.Suspended || key.ExpiresAt <= DateTime.UtcNow ||
            !key.AllowsIP(context.Connection.RemoteIpAddress))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return AuthMethodResult.Error;
        }

        // TODO: is there some way to cache user groups for API keys?
        await key.User.ComputeUserGroups(database);
        var groups = key.User.AccessCachedGroupsOrThrow();
        var denialReason = APIKeyAuthorization.GetDenialReason(key, context, groups);
        if (denialReason != null)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(denialReason);
            return AuthMethodResult.Error;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString();

        if (string.IsNullOrEmpty(remoteIp))
            throw new InvalidOperationException("No remote IP address");

        var usedAt = DateTime.UtcNow;

        // Atomic increment keeps simultaneous requests from losing usage counts.
        // Key is verified already, so do not use hashing.
        await database.APIKeys.Where(k => k.Id == key.Id).ExecuteUpdateAsync(update => update
            .SetProperty(k => k.TotalUses, k => k.TotalUses + 1)
            .SetProperty(k => k.LastUsedIP, remoteIp)
            .SetProperty(k => k.LastUsedAt, usedAt));

        context.Items[AppInfo.AuthenticationKeyMiddleWareKey] = key;
        OnAuthenticationSucceeded(context, key.User, AuthenticationScopeRestriction.None, null);
        return AuthMethodResult.Authenticated;
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

        if (link?.User == null || link.User.Suspended)
        {
            RememberFailedAuthentication(cacheKey);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(new BasicJSONErrorResult("Invalid token", "Access token is invalid")
                .ToString());
            return AuthMethodResult.Error;
        }

        // TODO: this should probably be removed? or not used just here (as this should never happen)
        if (context.Connection.RemoteIpAddress == null)
        {
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(new BasicJSONErrorResult("Internal server error",
                    "Internal server error when getting remote address")
                .ToString());
            return AuthMethodResult.Error;
        }

        var groups = link.CachedUserGroups;
        bool needsSave = false;

        if (groups == null)
        {
            // Recompute groups here as the link model allows groups to be null (which is different from the main
            // sessions where the opposite design is used)
            await link.User.ComputeUserGroups(database);
            link.CachedUserGroups = link.User.AccessCachedGroupsOrThrow();
            groups = link.CachedUserGroups;
            needsSave = true;
        }
        else
        {
            link.User.SetGroupsFromLauncherLinkCache(groups);
        }

        // Ensure the user still has access to launcher linking
        if (!groups.HasAccessLevel(GroupType.Developer) && !groups.HasGroup(GroupType.PatreonSupporter))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(new BasicJSONErrorResult("No launcher access",
                    "Your account does not have launcher access to the DevCenter")
                .ToString());

            if (needsSave)
            {
                // Save the computed groups to the database
                await database.SaveChangesAsync();
            }

            return AuthMethodResult.Error;
        }

        // As total API calls are updated anyway, the last connection and last IP are updated at the same time
        link.LastConnection = DateTime.UtcNow;
        link.LastIp = context.Connection.RemoteIpAddress.ToString();
        link.TotalApiCalls += 1;

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

                // When inside a cookie, CSRF needs to have passed (except for some download endpoints)
                context.Items[AppInfo.CSRFNeededName] = true;

                if (user.Suspended != true)
                {
                    // Ensure groups in session are up to date, if not up to date, update them
                    if (sessionObject.CachedUserGroups == null)
                    {
                        // Database data problem, UpdateUserGroupCacheJob should always keep the groups in sessions up
                        // to date
                        logger.LogError("Database session data is incorrect for user: {UserId}, session: {Session}",
                            user.Id, sessionObject.Id);
                        return AuthMethodResult.Nothing;
                    }

                    // Can use session-cached groups
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
