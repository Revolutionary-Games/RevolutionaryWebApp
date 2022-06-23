namespace ThriveDevCenter.Server.Services;

using Microsoft.Extensions.Caching.Memory;
using Shared;

/// <summary>
///   Memory cache with size limiting
/// </summary>
public class CustomMemoryCache
{
    public MemoryCache Cache { get; } = new(
        new MemoryCacheOptions
        {
            SizeLimit = AppInfo.MaxNormalCacheSize,
        });
}
