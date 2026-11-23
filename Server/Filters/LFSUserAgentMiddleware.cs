namespace RevolutionaryWebApp.Server.Filters;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

public class LFSUserAgentMiddleware
{
    private readonly RequestDelegate next;

    public LFSUserAgentMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var isLfsRequest = context.Request.Path.StartsWithSegments("/api/v1/lfs") ||
            context.Request.Path.StartsWithSegments("/api/v1/download_lfs");

        if (isLfsRequest && context.Request.Headers.TryGetValue("User-Agent", out var userAgents) &&
            userAgents.Any(userAgent =>
                userAgent?.Contains("facebookexternalhit", StringComparison.OrdinalIgnoreCase) == true))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("Facebook is downloading way more data than is reasonable");
            return;
        }

        await next(context);
    }
}
