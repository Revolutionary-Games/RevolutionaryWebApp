namespace RevolutionaryWebApp.Server.Authorization;

using System;
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
        var endpoint = context.GetEndpoint();
        var scope = endpoint?.Metadata.GetMetadata<APIKeyScopeAttribute>();

        var writes = endpoint?.Metadata.GetMetadata<APIKeyWriteAttribute>()?.Writes ??
            !HttpMethods.IsGet(context.Request.Method);

        return !key.User.Suspended && (key.ExpiresAt == null || key.ExpiresAt > DateTime.UtcNow) &&
            key.AllowsIP(context.Connection.RemoteIpAddress) && !groups.HasGroup(GroupType.RestrictedUser) &&
            scope != null && key.AllowsScope(scope.Scope) &&
            (!writes || (!key.ReadOnly && !APIKeyPolicy.IsReadOnlyUser(groups)));
    }
}
