namespace RevolutionaryWebApp.Server.Common.Services;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
///   Interface for managing the cache of running jobs
/// </summary>
public interface IExecutorCache
{
    /// <summary>
    ///   Calculates the size of the total cache on disk and in podman
    /// </summary>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>Used cache size in bytes</returns>
    public Task<long> CalculateCacheSizeAsync(CancellationToken cancellationToken);

    /// <summary>
    ///   Prunes cache items to get below the given size. If all should be deleted, pass in 0. Also deletes podman
    ///   images that are likely Thrive-related (or all, if this cannot get below the given size). This tries to keep
    ///   the latest items and prefers the master / main branch names in caches.
    /// </summary>
    /// <param name="cachePreserveSize">Max size in bytes of things to keep</param>
    /// <param name="cancellationToken">Cancellation of the operation</param>
    /// <returns>Task that gives the retained cache size</returns>
    public Task<long> PruneCacheAsync(long cachePreserveSize, CancellationToken cancellationToken);
}
