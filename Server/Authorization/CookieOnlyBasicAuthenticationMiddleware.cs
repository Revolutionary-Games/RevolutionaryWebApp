namespace ThriveDevCenter.Server.Authorization
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Models;
    using Shared;

    /// <summary>
    ///   Uses just cookies without a CSRF token for authorization. Used for Hangfire dashboard
    ///   (which hopefully has its own CSRF). And in the future probably for the download endpoint?
    /// </summary>
    public class CookieOnlyBasicAuthenticationMiddleware : BaseAuthenticationHelper
    {
        private readonly ApplicationDbContext database;

        public CookieOnlyBasicAuthenticationMiddleware(ApplicationDbContext database)
        {
            this.database = database;
        }

        protected override async Task<bool> PerformAuthentication(HttpContext context)
        {
            if (context.Request.Cookies.TryGetValue(AppInfo.SessionCookieName, out string session) &&
                !string.IsNullOrEmpty(session))
            {
                var user = await context.Request.Cookies.GetUserFromSession(database,
                    context.Connection.RemoteIpAddress);

                if (user != null)
                {
                    // This is special handling, and doesn't require CSRF

                    if (user.Suspended != true)
                    {
                        OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.None);
                        return true;
                    }

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Invalid session cookie");
                    return false;
                }
            }

            return true;
        }
    }
}
