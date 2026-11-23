namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Services;
using Xunit;

public sealed class LocalTempFileLocksTests : IDisposable
{
    private readonly string testTempDir;
    private readonly LocalTempFileLocks fileLocks;

    public LocalTempFileLocksTests()
    {
        testTempDir = Path.Combine(Path.GetTempPath(), "revwebapp-filelocks-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(testTempDir);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TempFileStorage:Path"] = testTempDir,
        }).Build();

        fileLocks = new LocalTempFileLocks(NullLogger<LocalTempFileLocks>.Instance, configuration);
    }

    [Fact]
    public void GetTempFilePath_ReturnsJoinedPath()
    {
        var result = fileLocks.GetTempFilePath("subfolder/file.txt");
        Assert.Equal(Path.Join(testTempDir, "subfolder/file.txt"), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/absolute/path")]
    public void GetTempFilePath_ThrowsOnInvalidSuffix(string invalidSuffix)
    {
        Assert.Throws<ArgumentException>(() => fileLocks.GetTempFilePath(invalidSuffix));
    }

    [Fact]
    public async Task LockAsync_LocksAndReleases()
    {
        var path = fileLocks.GetTempFilePath("lock-test-1");

        using (await fileLocks.LockAsync(path))
        {
            // Acquired lock successfully
        }
    }

    [Fact]
    public async Task LockAsync_WithCancellationToken_CanLockAndCancel()
    {
        var path = fileLocks.GetTempFilePath("lock-test-2");

        using var cts = new CancellationTokenSource();
        using (await fileLocks.LockAsync(path, cts.Token))
        {
            // Acquired lock successfully
        }

        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await fileLocks.LockAsync(path, cts.Token);
        });
    }

    [Fact]
    public async Task LockAsync_WithTimeout_ReturnsNullWhenTimeoutExpires()
    {
        var path = fileLocks.GetTempFilePath("lock-test-3");

        using (await fileLocks.LockAsync(path))
        {
            // Attempt to acquire lock for same path with very short timeout
            var secondLock = await fileLocks.LockAsync(path, TimeSpan.FromMilliseconds(50), CancellationToken.None);
            Assert.Null(secondLock);
        }

        // After releasing, should be acquirable
        var acquired = await fileLocks.LockAsync(path, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.NotNull(acquired);
        acquired.Dispose();
    }

    [Fact]
    public async Task LockAsync_SerializesConcurrentAccessSameKey()
    {
        var path = fileLocks.GetTempFilePath("lock-test-4");
        var counter = 0;

        async Task Worker()
        {
            for (var i = 0; i < 20; ++i)
            {
                using (await fileLocks.LockAsync(path))
                {
                    var current = counter;
                    await Task.Delay(2);
                    counter = current + 1;
                }
            }
        }

        var t1 = Task.Run(Worker);
        var t2 = Task.Run(Worker);
        await Task.WhenAll(t1, t2);

        Assert.Equal(40, counter);
    }

    public void Dispose()
    {
        fileLocks.Dispose();

        try
        {
            if (Directory.Exists(testTempDir))
                Directory.Delete(testTempDir, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
