namespace ThriveDevCenter.Server.Authorization;

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Models;
using Services;
using Shared;

/// <summary>
///   Uses just cookies without a CSRF token for authorization. Used for Hangfire dashboard
///   (which hopefully has its own CSRF). And in the future probably for the download endpoint?
/// </summary>
public class CookieOnlyBasicAuthenticationMiddleware : BaseAuthenticationHelper
{
    private readonly ApplicationDbContext database;

    public CookieOnlyBasicAuthenticationMiddleware(ApplicationDbContext database, CustomMemoryCache memoryCache) :
        base(memoryCache)
    {
        this.database = database;
    }

    protected override async Task<bool> PerformAuthentication(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(AppInfo.SessionCookieName, out string? session) &&
            !string.IsNullOrEmpty(session))
        {
            var cacheKey = "sessionKey:" + session;

            // Don't load DB data if we remember this data being bad
            if (IsNegativeAuthenticationAttemptCached(cacheKey))
            {
                return true;
            }

            var (user, sessionObject) = await context.Request.Cookies.GetUserFromSession(database,
                context.Connection.RemoteIpAddress);

            if (user != null)
            {
                // This is special handling, and doesn't require CSRF

                if (user.Suspended != true)
                {
                    OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None, sessionObject);
                    return true;
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Invalid session cookie");
                return false;
            }

            RememberFailedAuthentication(cacheKey);
        }

        return true;
    }
}
