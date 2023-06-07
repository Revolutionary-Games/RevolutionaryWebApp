namespace ThriveDevCenter.Server.Authorization;

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using Models;
using Services;
using Shared;
using Shared.Models;
using Utilities;

public class LFSAuthenticationMiddleware : BaseAuthenticationHelper
{
    private readonly ApplicationDbContext database;
    private readonly CustomMemoryCache cache;

    public LFSAuthenticationMiddleware(ApplicationDbContext database, CustomMemoryCache cache) : base(cache)
    {
        this.database = database;
        this.cache = cache;
    }

    protected override async Task<bool> PerformAuthentication(HttpContext context)
    {
        // Check Authorization header for lfs token in "Basic base64encoded" format
        if (!context.Request.Headers.TryGetValue("Authorization", out StringValues header) || header.Count <= 0)
            return true;

        var tokenValue = header[0];

        if (tokenValue == null || !tokenValue.StartsWith("Basic "))
        {
            await WriteGitLFSJsonError(context, "Invalid format for Authorization header", true);
            return false;
        }

        var encoded = tokenValue.Split(' ').LastOrDefault();

        if (encoded == null)
        {
            await WriteGitLFSJsonError(context, "Invalid format for Authorization header", true);
            return false;
        }

        string userPassword;
        try
        {
            var base64EncodedBytes = Convert.FromBase64String(encoded);
            userPassword = Encoding.UTF8.GetString(base64EncodedBytes);
        }
        catch (Exception)
        {
            await WriteGitLFSJsonError(context, "Invalid encoding of Authorization header", true);
            return false;
        }

        var parts = userPassword.Split(':');

        if (parts.Length != 2)
        {
            await WriteGitLFSJsonError(context, "Invalid format for Authorization header", true);
            return false;
        }

        if (parts[1].Length > 100)
        {
            await WriteGitLFSJsonError(context, "Invalid format for Authorization header (token is too long)", true);
            return false;
        }

        var errorMessage = "Invalid credentials to ThriveDevCenter (use your email and LFS token from your " +
            "ThriveDevCenter profile) or your account is suspended";

        var authNegativeCacheKey = "lfs:" + parts[1];

        // Don't load DB data if we remember this data being bad
        if (IsNegativeAuthenticationAttemptCached(authNegativeCacheKey))
        {
            await WriteGitLFSJsonError(context, errorMessage, false);
            return false;
        }

        var user = await database.Users.WhereHashed(nameof(User.LfsToken), parts[1]).AsAsyncEnumerable()
            .FirstOrDefaultAsync(u => u.LfsToken == parts[1]);

        // The given "username" part of the basic auth needs to either match the email or name of the found user
        if (user != null && user.Suspended != true && (user.UserName == parts[0] || user.Email == parts[0]))
        {
            // Need to load user groups for this to work, we use a short cache time here to reduce the DB loads needed
            var cacheKey = $"userGroups/{user.Id}";

            if (cache.Cache.TryGetValue(cacheKey, out object? rawCacheEntry) &&
                rawCacheEntry is CachedUserGroups cachedGroups)
            {
                // Not exactly the method meant for this, but logically this does the same thing as the name of the
                // method implies it is meant for
                user.SetGroupsFromLauncherLinkCache(cachedGroups);
            }
            else
            {
                await user.ComputeUserGroups(database);

                // Store the cached groups for later access
                cachedGroups = user.AccessCachedGroupsOrThrow();

                var cacheEntryOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromSeconds(60))
                    .SetSize(cachedGroups.Groups.Count() * sizeof(long) + cacheKey.Length);

                cache.Cache.Set(cacheKey, cachedGroups, cacheEntryOptions);
            }

            OnAuthenticationSucceeded(context, user, AuthenticationScopeRestriction.LFSOnly, null);
            return true;
        }

        RememberFailedAuthentication(authNegativeCacheKey);

        // If the token is incorrect we'll want to fail with 403 to not cause infinite retries in LFS clients
        await WriteGitLFSJsonError(context, errorMessage, false);
        return false;
    }

    /// <summary>
    ///   Sets the error for LFS login problem in LFS request
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     If this is edited the error response in <see cref="Controllers.LFSController"/> should be edited
    ///     as well.
    ///   </para>
    /// </remarks>
    private static Task WriteGitLFSJsonError(HttpContext context, string error, bool badRequest)
    {
        context.Response.ContentType = AppInfo.GitLfsContentType;

        if (badRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
        else
        {
            context.Response.Headers["LFS-Authenticate"] = "Basic realm=\"ThriveDevCenter Git LFS\"";
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        }

        return context.Response.WriteAsync(
            new GitLFSErrorResponse { Message = error }.ToString());
    }
}
