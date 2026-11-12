namespace RevolutionaryWebApp.Server.Tests.Models.Tests;

using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Notifications;
using Xunit;

public class TranslationProjectAdvisoryLockRealDbTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;

    public TranslationProjectAdvisoryLockRealDbTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public async Task SyncStateIsVisibleAcrossConnectionsAndClearedBeforeReleaseNotification(long projectId)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.GetConnectionStringForAdditionalDatabase()).Options;
        await using var worker = new ApplicationDbContext(options);
        await using var viewer = new ApplicationDbContext(options);
        var sender = Substitute.For<IModelUpdateNotificationSender>();
        var statesAtNotification = new List<bool>();
        sender.SendNotifications(Arg.Any<IEnumerable<Tuple<SerializedNotification, string>>>())
            .Returns(async _ =>

                // Each notification is awaited before the viewer context is disposed.
                // ReSharper disable once AccessToDisposedClosure
                statesAtNotification.Add(await TranslationProjectAdvisoryLock.IsHeldAsync(viewer, projectId)));

        Assert.False(await TranslationProjectAdvisoryLock.IsHeldAsync(viewer, projectId));
        await using (await TranslationProjectAdvisoryLock.AcquireAsync(worker, projectId))
        {
            // Normal saves must not show the long-running operation warning.
            Assert.False(await TranslationProjectAdvisoryLock.IsHeldAsync(viewer, projectId));
        }

        await using (await TranslationProjectAdvisoryLock.AcquireForSyncAsync(worker, projectId, sender))
        {
            Assert.True(await TranslationProjectAdvisoryLock.IsHeldAsync(viewer, projectId));
            Assert.False(await TranslationProjectAdvisoryLock.IsHeldAsync(viewer, -1));
        }

        Assert.False(await TranslationProjectAdvisoryLock.IsHeldAsync(viewer, projectId));
        Assert.Equal(new[] { true, false }, statesAtNotification);
    }

    [Fact]
    public async Task RepositorySyncSerializesWorkersWithoutBlockingEditsOrClosingNestedConnections()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.GetConnectionStringForAdditionalDatabase()).Options;
        await using var worker = new ApplicationDbContext(options);
        await using var otherWorker = new ApplicationDbContext(options);
        var sender = Substitute.For<IModelUpdateNotificationSender>();
        const long projectId = 1234;

        await using (await TranslationProjectAdvisoryLock.AcquireForRepositorySyncAsync(worker, projectId))
        {
            Assert.False(await TranslationProjectAdvisoryLock.IsHeldAsync(otherWorker, projectId));
            await using (await TranslationProjectAdvisoryLock.AcquireForSyncAsync(worker, projectId, sender))
            {
                Assert.True(await TranslationProjectAdvisoryLock.IsHeldAsync(otherWorker, projectId));
            }

            Assert.Equal(ConnectionState.Open, worker.Database.GetDbConnection().State);
            await using (await TranslationProjectAdvisoryLock.AcquireAsync(otherWorker, projectId))
            {
                Assert.False(await TranslationProjectAdvisoryLock.IsHeldAsync(otherWorker, projectId));
            }

            await using (await TranslationProjectAdvisoryLock.AcquireForRepositorySyncAsync(otherWorker, -projectId))
            {
                // Other projects can synchronize independently.
            }

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                TranslationProjectAdvisoryLock.AcquireForRepositorySyncAsync(otherWorker, projectId, timeout.Token));
        }

        Assert.Equal(ConnectionState.Closed, worker.Database.GetDbConnection().State);
        await using var nextSync = await TranslationProjectAdvisoryLock.AcquireForRepositorySyncAsync(otherWorker,
            projectId);
    }
}
