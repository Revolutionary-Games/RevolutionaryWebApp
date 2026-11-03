namespace RevolutionaryWebApp.Server.Authorization;

using System;

/// <summary>
///   Allows overwriting whether API key methods write or not (default is only GET is a read operation)
/// </summary>
/// <param name="writes">True when this is a write operation</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class APIKeyWriteAttribute(bool writes = true) : Attribute
{
    public bool Writes { get; } = writes;
}
