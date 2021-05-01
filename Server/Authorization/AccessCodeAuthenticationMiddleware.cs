namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared;
    using Utilities;

    public class AccessCodeAuthenticationMiddleware : IMiddleware
    {
        private readonly ApplicationDbContext database;

        public AccessCodeAuthenticationMiddleware(ApplicationDbContext database)
        {
            this.database = database;
        }

        public async Task InvokeAsync(HttpContext context, RequestDelegate next)
        {
            if (context.Request.Headers.TryGetValue("X-Access-Code", out StringValues headerValues))
            {
                if (headerValues.Count < 1 || string.IsNullOrEmpty(headerValues[0]))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("X-Access-Code header is empty");
                    return;
                }

                var usedCode =
                    await database.AccessKeys.WhereHashed(nameof(AccessKey.KeyCode), headerValues[0])
                        .ToAsyncEnumerable().FirstOrDefaultAsync(k => k.KeyCode == headerValues[0]);

                if (usedCode == null)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsync("Access code is invalid");
                    return;
                }

                var now = DateTime.UtcNow;
                var clientAddress = context.Connection.RemoteIpAddress;

                // For performance optimization, last used isn't updated always
                if (usedCode.LastUsed == null ||
                    now - usedCode.LastUsed >= AppInfo.LastUsedAccessKeyAccuracy ||
                    usedCode.LastUsedFrom == null ||
                    !usedCode.LastUsedFrom.Equals(clientAddress))
                {
                    usedCode.LastUsed = now;
                    usedCode.LastUsedFrom = clientAddress;
                    await database.SaveChangesAsync();
                }

                context.Items[AppInfo.AccessKeyMiddlewareKey] = usedCode;
            }

            await next.Invoke(context);
        }
    }
}
