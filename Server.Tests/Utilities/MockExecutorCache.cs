namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System;
using System.Threading;
using System.Threading.Tasks;
using Common.Services;

/// <summary>
///   Mock implementation of <see cref="IExecutorCache" />
/// </summary>
public class MockExecutorCache : IExecutorCache
{
    public long AutoIncrementEachTime { get; set; }

    public long ReportedSize { get; set; }

    public int CleanedTimes { get; set; }

    public Task<long> CalculateCacheSizeAsync(CancellationToken cancellationToken)
    {
        var value = ReportedSize;

        ReportedSize += AutoIncrementEachTime;

        return Task.FromResult(value);
    }

    public Task<long> PruneCacheAsync(long cachePreserveSize, CancellationToken cancellationToken)
    {
        if (cachePreserveSize < 0)
            throw new ArgumentException("Cache preserve size cannot be negative", nameof(cachePreserveSize));

        ++CleanedTimes;

        ReportedSize = Math.Min(cachePreserveSize, ReportedSize);

        return Task.FromResult(ReportedSize);
    }
}
