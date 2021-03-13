namespace ThriveDevCenter.Server.Authorization
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Primitives;

    public class CSRFCheckerMiddleware : IMiddleware
    {
        private readonly JwtTokens csrfVerifier;

        public CSRFCheckerMiddleware(JwtTokens csrfVerifier)
        {
            this.csrfVerifier = csrfVerifier;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            context.Items["CSRF"] = false;
            if (context.Request.Headers.TryGetValue("X-CSRF-Token", out StringValues headerValues))
            {
                if (headerValues.Count < 1 || string.IsNullOrEmpty(headerValues[0]))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("CSRF token header is empty");
                    return;
                }

                if (!csrfVerifier.IsValidCSRFToken(headerValues[0]))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("CSRF token is invalid. Please refresh and try again.");
                }

                context.Items["CSRF"] = true;
            }

            await next.Invoke(context);
        }
    }
}
