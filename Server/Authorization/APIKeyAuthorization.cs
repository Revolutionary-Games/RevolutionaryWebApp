namespace RevolutionaryWebApp.Server.Authorization;

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Models;
using Shared.Models;
using Shared.Models.Enums;

/// <summary>
///   Checks if the API key is allowed to perform the request
/// </summary>
public static class APIKeyAuthorization
{
    public static bool AllowsRequest(APIKey key, HttpContext context, IUserGroupData groups)
    {
        return GetDenialReason(key, context, groups) == null;
    }

    /// <summary>
    ///   Returns null when allowed, otherwise a description suitable for the forbidden response.
    /// </summary>
    public static string? GetDenialReason(APIKey key, HttpContext context, IUserGroupData groups)
    {
        if (key.User.Suspended || key.ExpiresAt <= DateTime.UtcNow ||
            !key.AllowsIP(context.Connection.RemoteIpAddress) || groups.HasGroup(GroupType.RestrictedUser))
        {
            return "API key access is disabled or restricted.";
        }

        var endpoint = context.GetEndpoint();
        var scope = endpoint?.Metadata.GetMetadata<APIKeyScopeAttribute>();
        if (scope == null)
            return "This operation does not allow API key authentication.";

        var reasons = new List<string>();
        if (!key.AllowsScope(scope.Scope))
        {
            reasons.Add("API key has incorrect scopes. " +
                $"This operation requires scope {scope.Scope} ({(int)scope.Scope}).");
        }

        var writes = endpoint?.Metadata.GetMetadata<APIKeyWriteAttribute>()?.Writes ??
            !HttpMethods.IsGet(context.Request.Method);
        if (writes)
        {
            if (key.ReadOnly)
            {
                reasons.Add("API key is read-only, but this operation requires write access.");
            }
            else if (APIKeyPolicy.IsReadOnlyUser(groups))
            {
                reasons.Add("API keys for this user are restricted to read-only access, " +
                    "but this operation requires write access.");
            }
        }

        return reasons.Count == 0 ? null : string.Join(' ', reasons);
    }
}
