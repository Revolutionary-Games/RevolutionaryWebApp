namespace RevolutionaryWebApp.Server.Authorization;

using System;
using Shared.Models.Enums;

/// <summary>
///   Specifies the scope this endpoint is for in terms of API usage
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class APIKeyScopeAttribute : Attribute
{
    /// <summary>
    ///   Allows any API key scope, for endpoints that inspect the current key itself.
    /// </summary>
    public APIKeyScopeAttribute()
    {
    }

    public APIKeyScopeAttribute(APIKeyScope scope)
    {
        Scope = scope;
    }

    public APIKeyScope? Scope { get; }
}
