namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Dummies;
using Fixtures;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ClearExtensions;
using Server.Jobs;
using Server.Models;
using Server.Services;
using Shared.Notifications;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class CIImageTests : IDisposable
{
    private readonly XunitLogger<LockCIImageItemJob> logger;

    public CIImageTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<LockCIImageItemJob>(output);
    }

    [Fact]
    public async Task LockCIImageItem_DeletesRightVersions()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var database =
            new EditableInMemoryDatabaseFixtureWithNotifications(notificationsMock,
                "LockCIImageDeleteRightVersion");

        var item1Version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = true,
        };

        var item1Version2 = new StorageItemVersion
        {
            Version = 2,
            Uploading = false,
        };

        var imageItem1 = new StorageItem
        {
            Name = "CIImage1",
            Ftype = FileType.File,
            AllowParentless = true,
            StorageItemVersions = new HashSet<StorageItemVersion>
            {
                item1Version1,
                item1Version2,
            },
        };

        item1Version1.StorageItem = imageItem1;
        item1Version2.StorageItem = imageItem1;

        var item2Version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = true,
        };

        var item2Version2 = new StorageItemVersion
        {
            Version = 2,
            Uploading = false,
        };

        var imageItem2 = new StorageItem
        {
            Name = "CIImage2",
            Ftype = FileType.File,
            AllowParentless = true,
            StorageItemVersions = new HashSet<StorageItemVersion>
            {
                item2Version1,
                item2Version2,
            },
        };

        item2Version1.StorageItem = imageItem2;
        item2Version2.StorageItem = imageItem2;

        database.Database.StorageItems.Add(imageItem1);
        database.Database.StorageItemVersions.Add(item1Version1);
        database.Database.StorageItemVersions.Add(item1Version2);

        database.Database.StorageItems.Add(imageItem2);
        database.Database.StorageItemVersions.Add(item2Version1);
        database.Database.StorageItemVersions.Add(item2Version2);

        await database.Database.SaveChangesAsync();
        notificationsMock.ClearSubstitute();

        var dummyList = new List<Tuple<SerializedNotification, string>>
        {
            new(new DummyUpdated(), DummyUpdated.UpdateGroup),
        };

        notificationsMock.OnChangesDetected(EntityState.Modified, imageItem1, false)
            .Returns(dummyList);

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var job = new LockCIImageItemJob(logger, database.NotificationsEnabledDatabase, jobClientMock);

        Assert.False(imageItem1.Special);
        Assert.Equal(FileAccess.Developer, imageItem1.WriteAccess);
        Assert.False(item1Version1.Keep);
        Assert.False(item1Version1.Protected);
        Assert.False(item1Version2.Keep);
        Assert.False(item1Version2.Protected);

        Assert.False(imageItem2.Special);
        Assert.Equal(FileAccess.Developer, imageItem2.WriteAccess);
        Assert.False(item2Version1.Keep);
        Assert.False(item2Version1.Protected);
        Assert.False(item2Version2.Keep);
        Assert.False(item2Version2.Protected);

        await job.Execute(imageItem1.Id, CancellationToken.None);

        Assert.True(imageItem1.Special);
        Assert.Equal(FileAccess.Nobody, imageItem1.WriteAccess);

        // Right version is marked to be kept (while the other should have gotten marked as delete, but we can't
        // mock IBackgroundJobClient well enough here to detect that
        Assert.False(item1Version1.Keep);
        Assert.False(item1Version1.Protected);
        Assert.True(item1Version2.Keep);
        Assert.True(item1Version2.Protected);

        // Unrelated item is not modified
        Assert.False(imageItem2.Special);
        Assert.Equal(FileAccess.Developer, imageItem2.WriteAccess);
        Assert.False(item2Version1.Keep);
        Assert.False(item2Version1.Protected);
        Assert.False(item2Version2.Keep);
        Assert.False(item2Version2.Protected);

        notificationsMock.Received().OnChangesDetected(EntityState.Modified, imageItem1, false);
        await notificationsMock.Received()
            .SendNotifications(Arg.Is<IEnumerable<Tuple<SerializedNotification, string>>>(l => l.Any()));
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
