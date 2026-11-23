namespace RevolutionaryWebApp.Server.Tests.Models.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared.Notifications;
using Xunit;

public class TranslationProjectAdvisoryLockTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SyncNotifiesOnAcquisitionAndReleaseEvenWhenOperationFails(bool fail)
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var sender = Substitute.For<IModelUpdateNotificationSender>();
        var updates = new List<TranslationProjectUpdated>();
        sender.SendNotifications(Arg.Any<IEnumerable<Tuple<SerializedNotification, string>>>())
            .Returns(call =>
            {
                var notification = Assert.Single(call.Arg<IEnumerable<Tuple<SerializedNotification, string>>>());
                Assert.Equal(NotificationGroups.TranslationProjectUpdatedPrefix + 42, notification.Item2);
                updates.Add(Assert.IsType<TranslationProjectUpdated>(notification.Item1));
                return Task.CompletedTask;
            });

        try
        {
            await using var projectLock = await TranslationProjectAdvisoryLock.AcquireForSyncAsync(database, 42,
                sender);
            Assert.Single(updates);
            if (fail)
                throw new InvalidOperationException("Operation failed");
        }
        catch (InvalidOperationException) when (fail)
        {
        }

        Assert.Equal(2, updates.Count);
        Assert.All(updates, update => Assert.Equal(42, update.ProjectId));
    }
}
