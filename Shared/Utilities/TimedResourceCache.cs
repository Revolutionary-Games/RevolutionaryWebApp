namespace RevolutionaryWebApp.Shared.Utilities;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///   Allows caching a specific resource for some time
/// </summary>
/// <typeparam name="T">The object type to cache</typeparam>
public class TimedResourceCache<T> : IDisposable
    where T : class
{
    private readonly SemaphoreSlim generationLock = new(1);

    private readonly Func<Task<T>> generateData;
    private readonly TimeSpan cacheTime;

    private DateTime lastGenerated;
    private T? data;

    public TimedResourceCache(Func<Task<T>> generateData, TimeSpan cacheTime)
    {
        this.generateData = generateData;
        this.cacheTime = cacheTime;
    }

    public async Task<T> GetData()
    {
        var lockTask = generationLock.WaitAsync();
        var now = DateTime.UtcNow;

        await lockTask;

        try
        {
            if (data == null || now - lastGenerated > cacheTime)
            {
                data = await generateData();
                lastGenerated = now;
            }

            return data;
        }
        finally
        {
            generationLock.Release();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            generationLock.Dispose();
            data = null;
        }
    }
}

/// <summary>
///   Variant of the cache that stores disposable resources
/// </summary>
/// <typeparam name="T">The object type to cache</typeparam>
public class DisposableTimedResourceCache<T> : IDisposable
    where T : class, IDisposable
{
    private readonly SemaphoreSlim generationLock = new(1);

    private readonly Func<Task<T>> generateData;
    private readonly TimeSpan cacheTime;

    private DateTime lastGenerated;
    private T? data;

    private bool disposed;

    public DisposableTimedResourceCache(Func<Task<T>> generateData, TimeSpan cacheTime)
    {
        this.generateData = generateData;
        this.cacheTime = cacheTime;
    }

    public async Task<T> GetData()
    {
        var lockTask = generationLock.WaitAsync();
        var now = DateTime.UtcNow;

        await lockTask;

        try
        {
            if (data == null || now - lastGenerated > cacheTime)
            {
                if (data != null)
                {
                    data.Dispose();
                    data = null;
                }

                data = await generateData();
                lastGenerated = now;
            }

            return data;
        }
        finally
        {
            generationLock.Release();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
            return;

        if (disposing)
        {
            generationLock.Dispose();
            data?.Dispose();
            data = null;
        }

        disposed = true;
    }
}
