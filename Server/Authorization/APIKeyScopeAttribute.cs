namespace RevolutionaryWebApp.Server.Authorization;

using System;
using Shared.Models.Enums;

/// <summary>
///   Specifies the scope this endpoint is for in terms of API usage
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class APIKeyScopeAttribute(APIKeyScope scope) : Attribute
{
    public APIKeyScope Scope { get; } = scope;
}
