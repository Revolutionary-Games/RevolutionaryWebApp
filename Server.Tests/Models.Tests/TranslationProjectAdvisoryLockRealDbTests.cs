namespace RevolutionaryWebApp.Server.Tests.Models.Tests;

using System;
using System.Collections.Generic;
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
}
