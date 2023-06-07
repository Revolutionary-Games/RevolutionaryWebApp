namespace ThriveDevCenter.Server.Filters;

using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Models;
using SharedBase.Utilities;

public class CustomRateLimiter
{
    public static Func<OnRejectedContext, CancellationToken, ValueTask> OnRejected { get; } =
        async (context, token) =>
        {
            context.HttpContext.Response.StatusCode = 429;

            if (context.Lease.TryGetMetadata(MetadataName.ReasonPhrase, out var reason))
            {
                context.HttpContext.Response.Headers.Add("X-RateLimit-Reason", reason);
            }

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                context.HttpContext.Response.Headers.Add(HeaderNames.RetryAfter,
                    seconds.ToString(CultureInfo.InvariantCulture));

                await context.HttpContext.Response.WriteAsync(
                    $"Too many requests. Please try again in {"second".PrintCount(seconds)}.", token);
            }
            else
            {
                await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later.", token);
            }
        };

    public static PartitionedRateLimiter<HttpContext> CreateGlobalLimiter(MyRateLimitOptions limitOptions)
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var httpRequest = httpContext.Request;
            bool isGet = httpRequest.Method == HttpMethods.Get ||
                httpRequest.Method == HttpMethods.Options || httpRequest.Method == HttpMethods.Head;

            var methodString = isGet ? "get:" : "post:";

            // TODO: implement user info reading from the request (normal auth flow is not performed yet)
            // probably better to move the auth reading to be earlier but add a tiny bit of memory caching there
            var user = httpContext.AuthenticatedUser();

            if (user == null)
            {
                // TODO: check for specific access keys like ones used to upload devbuilds

                // Anonymous access
                var ip = httpContext.Connection.RemoteIpAddress;

                string ipStr;
                if (ip != null)
                {
                    // Access from server localhost is unlimited
                    if (limitOptions.AllowUnlimitedFromLocalhost && IPAddress.IsLoopback(ip))
                        return RateLimitPartition.GetNoLimiter<string>("loopback");

                    // Microsoft documentation doesn't recommend to partition by IP, but we already have an nginx proxy
                    // in front of us, and there doesn't seem to be a recommended other approach (except screwing over all
                    // anonymous users if one is behaving badly)
                    ipStr = methodString + ip;
                }
                else
                {
                    ipStr = methodString + "unknownIp";
                }

                if (isGet)
                {
                    return CreateGetLimit(limitOptions, ipStr);
                }

                return CreatePostLimit(limitOptions, ipStr);
            }

            return CreateUserPartition(limitOptions, user, isGet, methodString);
        });
    }

    public static void CreateLoginLimiter(RateLimiterOptions limiterOptions, MyRateLimitOptions limitOptions)
    {
        limiterOptions.AddPolicy(RateLimitCategories.LoginLimit, httpContext =>
        {
            var partitionKey =
                RateLimitCategories.LoginLimit + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limitOptions.LoginAndRegistrationLimit,
                    Window = TimeSpan.FromSeconds(limitOptions.LoginWindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = limitOptions.ShortWindowQueueLimit,
                });
        });
    }

    private static RateLimitPartition<string> CreateGetLimit(MyRateLimitOptions limitOptions, string ip)
    {
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limitOptions.GlobalGetLimit,
                Window = TimeSpan.FromSeconds(limitOptions.GlobalWindowSeconds),
                SegmentsPerWindow = limitOptions.SegmentsPerWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = limitOptions.QueueLimit,
            });
    }

    private static RateLimitPartition<string> CreatePostLimit(MyRateLimitOptions limitOptions, string ip)
    {
        return RateLimitPartition.GetSlidingWindowLimiter(ip, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = limitOptions.GlobalPostLimit,
                Window = TimeSpan.FromSeconds(limitOptions.GlobalWindowSeconds),
                SegmentsPerWindow = limitOptions.SegmentsPerWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = limitOptions.QueueLimit,
            });
    }

    private static RateLimitPartition<string> CreateUserPartition(MyRateLimitOptions limitOptions, User user, bool get,
        string methodString)
    {
        var userPartition = string.Concat(methodString, "u:", user.Id);

        return RateLimitPartition.GetSlidingWindowLimiter(userPartition, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = get ? limitOptions.UserGlobalGetLimit : limitOptions.UserGlobalPostLimit,
                Window = TimeSpan.FromSeconds(limitOptions.GlobalWindowSeconds),
                SegmentsPerWindow = limitOptions.SegmentsPerWindow,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = limitOptions.QueueLimit,
            });
    }
}
