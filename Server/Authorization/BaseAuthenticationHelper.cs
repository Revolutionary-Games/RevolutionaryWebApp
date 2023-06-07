namespace ThriveDevCenter.Server.Authorization;

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Models;
using Services;
using Shared;

public abstract class BaseAuthenticationHelper : IMiddleware
{
    private readonly CustomMemoryCache memoryCache;

    public BaseAuthenticationHelper(CustomMemoryCache memoryCache)
    {
        this.memoryCache = memoryCache;
    }

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
        if (context.User.Identity == null || !context.Items.ContainsKey(AppInfo.CurrentUserMiddlewareKey))
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
        AuthenticationScopeRestriction restriction, Session? session)
    {
        if (user == null)
            throw new ArgumentException("can't set authenticated user to null");

        // Ensure groups are computed here as a bunch of code relies on authenticated user groups being available
        user.AccessCachedGroupsOrThrow();

        var identity = new ClaimsIdentity(user);

        context.User.AddIdentity(identity);
        context.Items[AppInfo.CurrentUserMiddlewareKey] = user;

        // When using cookie authentication, there exist a session for the login, we store it here for a few
        // special actions that use the knowledge of which of the user's session was used to authenticate
        context.Items[AppInfo.CurrentUserSessionMiddleWareKey] = session;
        context.Items[AppInfo.AuthenticationScopeRestrictionMiddleWareKey] = restriction;
    }

    protected bool IsNegativeAuthenticationAttemptCached(string key)
    {
        if (memoryCache.Cache.TryGetValue(key, out var value) && value is true)
        {
            return true;
        }

        return false;
    }

    protected void RememberFailedAuthentication(string key)
    {
        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(AppInfo.NegativeAuthCacheTime)
            .SetAbsoluteExpiration(DateTimeOffset.UtcNow + AppInfo.NegativeAuthCacheTimeMax)
            .SetPriority(CacheItemPriority.Low).SetSize(key.Length);

        memoryCache.Cache.Set(key, true, cacheEntryOptions);
    }
}
