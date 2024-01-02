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

            // This seems to only be global or endpoint, so not very useful
            // if (context.Lease.TryGetMetadata(MetadataName.ReasonPhrase, out var reason))
            // {
            //     context.HttpContext.Response.Headers.Add("X-RateLimit-Reason", reason);
            // }

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            {
                var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
                context.HttpContext.Response.Headers[HeaderNames.RetryAfter] =
                    seconds.ToString(CultureInfo.InvariantCulture);

                await context.HttpContext.Response.WriteAsync(
                    $"Too many requests. Please try again in {"second".PrintCount(seconds)}",
                    token);
            }
            else
            {
                await context.HttpContext.Response.WriteAsync("Too many requests. Please try again later", token);
            }
        };

    public static PartitionedRateLimiter<HttpContext> CreateGlobalLimiter(MyRateLimitOptions limitOptions)
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(
            httpContext =>
            {
                var httpRequest = httpContext.Request;
                bool isGet = httpRequest.Method == HttpMethods.Get ||
                    httpRequest.Method == HttpMethods.Options || httpRequest.Method == HttpMethods.Head;

                var methodString = isGet ? "get:" : "post:";

                // TODO: implement user info reading from the request (normal auth flow is not performed yet)
                // probably better to move the auth reading to be earlier but add a tiny bit of memory caching there
                var user = httpContext.AuthenticatedUser();

                if (user != null)
                    return CreateUserPartition(limitOptions, user, isGet, methodString);

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
                    // in front of us, and there doesn't seem to be a recommended other approach (except screwing over
                    // all anonymous users if one is behaving badly)
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
            });
    }

    public static void CreateLoginLimiter(RateLimiterOptions limiterOptions, MyRateLimitOptions limitOptions)
    {
        CreateAreaRelatedFixedLimiter(
            limiterOptions,
            RateLimitCategories.LoginLimit,
            limitOptions.LoginLimit,
            limitOptions.LoginWindowSeconds,
            limitOptions.ShortWindowQueueLimit);
    }

    public static void CreateRegistrationLimiter(RateLimiterOptions limiterOptions, MyRateLimitOptions limitOptions)
    {
        CreateAreaRelatedFixedLimiter(
            limiterOptions,
            RateLimitCategories.RegistrationLimit,
            limitOptions.RegistrationLimit,
            limitOptions.RegistrationWindowSeconds,
            limitOptions.ShortWindowQueueLimit);
    }

    public static void CreateCodeRedeemLimiter(RateLimiterOptions limiterOptions, MyRateLimitOptions limitOptions)
    {
        // TODO: this would make a lot more sense to be user specific, but this is good enough for now to prevent
        // hopefully most brute-forcing (if someone wanted to brute-force they could even have multiple accounts)
        CreateAreaRelatedFixedLimiter(
            limiterOptions,
            RateLimitCategories.CodeRedeemLimit,
            limitOptions.CodeRedeemLimit,
            limitOptions.CodeRedeemWindowSeconds,
            limitOptions.ShortWindowQueueLimit);
    }

    public static void CreateEmailVerificationLimiter(
        RateLimiterOptions limiterOptions,
        MyRateLimitOptions limitOptions)
    {
        CreateAreaRelatedTokenLimiter(
            limiterOptions,
            RateLimitCategories.EmailVerification,
            limitOptions.EmailVerificationTokens,
            limitOptions.EmailVerificationRefreshSeconds,
            limitOptions.EmailVerificationRefreshAmount);
    }

    public static void CreateCrashReportLimiter(RateLimiterOptions limiterOptions, MyRateLimitOptions limitOptions)
    {
        CreateAreaRelatedTokenLimiter(
            limiterOptions,
            RateLimitCategories.CrashReport,
            limitOptions.CrashReportTokens,
            limitOptions.CrashReportRefreshSeconds,
            limitOptions.CrashReportRefreshAmount);
    }

    public static void CreateStackwalkLimiter(RateLimiterOptions limiterOptions, MyRateLimitOptions limitOptions)
    {
        CreateAreaRelatedTokenLimiter(
            limiterOptions,
            RateLimitCategories.Stackwalk,
            limitOptions.StackwalkTokens,
            limitOptions.StackwalkRefreshSeconds,
            limitOptions.StackwalkRefreshAmount);
    }

    private static void CreateAreaRelatedFixedLimiter(
        RateLimiterOptions limiterOptions,
        string category,
        int limit,
        int windowSeconds,
        int queueSize)
    {
        limiterOptions.AddPolicy(
            category,
            httpContext =>
            {
                var partitionKey = category + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ =>
                        new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = limit,
                            Window = TimeSpan.FromSeconds(windowSeconds),
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = queueSize,
                        });
            });
    }

    private static void CreateAreaRelatedTokenLimiter(
        RateLimiterOptions limiterOptions,
        string category,
        int limit,
        int refreshSeconds,
        int refreshAmount)
    {
        limiterOptions.AddPolicy(
            category,
            httpContext =>
            {
                var partitionKey = category + (httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");

                return RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey,
                    _ =>
                        new TokenBucketRateLimiterOptions
                        {
                            ReplenishmentPeriod = TimeSpan.FromSeconds(refreshSeconds),
                            TokensPerPeriod = refreshAmount,
                            TokenLimit = limit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0,
                        });
            });
    }

    private static RateLimitPartition<string> CreateGetLimit(MyRateLimitOptions limitOptions, string ip)
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limitOptions.GlobalGetLimit,
                    Window = TimeSpan.FromSeconds(limitOptions.GlobalWindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = limitOptions.QueueLimit,
                });
    }

    private static RateLimitPartition<string> CreatePostLimit(MyRateLimitOptions limitOptions, string ip)
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            ip,
            _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limitOptions.GlobalPostLimit,
                    Window = TimeSpan.FromSeconds(limitOptions.GlobalWindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = limitOptions.QueueLimit,
                });
    }

    private static RateLimitPartition<string> CreateUserPartition(
        MyRateLimitOptions limitOptions,
        User user,
        bool get,
        string methodString)
    {
        var userPartition = string.Concat(methodString, "u:", user.Id);

        return RateLimitPartition.GetFixedWindowLimiter(
            userPartition,
            _ =>
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = get ? limitOptions.UserGlobalGetLimit : limitOptions.UserGlobalPostLimit,
                    Window = TimeSpan.FromSeconds(limitOptions.GlobalWindowSeconds),
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = limitOptions.QueueLimit,
                });
    }
}
