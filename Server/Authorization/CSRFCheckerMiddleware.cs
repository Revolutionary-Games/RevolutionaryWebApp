namespace ThriveDevCenter.Server.Authorization
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Services;
    using Shared;

    public class CSRFCheckerMiddleware : IMiddleware
    {
        private readonly ITokenVerifier csrfVerifier;

        public CSRFCheckerMiddleware(ITokenVerifier csrfVerifier)
        {
            this.csrfVerifier = csrfVerifier;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (context.Request.Headers.TryGetValue("X-CSRF-Token", out StringValues headerValues))
            {
                if (headerValues.Count < 1 || string.IsNullOrEmpty(headerValues[0]))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("CSRF token header is empty");
                    return;
                }

                User user = null;

                if (context.Items.TryGetValue(AppInfo.CurrentUserMiddlewareKey, out object userRaw))
                {
                    user = userRaw as User;
                }

                if (!csrfVerifier.IsValidCSRFToken(headerValues[0], user))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("CSRF token is invalid. Please refresh and try again.");
                    return;
                }

                context.Items[AppInfo.CSRFStatusName] = true;
            }
            else if (context.Items.ContainsKey(AppInfo.CSRFNeededName))
            {
                // Download endpoints (for usability with direct links, don't require this)
                if (!context.Request.Path.StartsWithSegments("/api/v1/download") &&
                    !context.Request.Path.StartsWithSegments("/api/v1/download_lfs"))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("CSRF token is required for this request.");
                    return;
                }
            }

            await next.Invoke(context);
        }
    }
}
