namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using NSubstitute;
using Server.Jobs;
using Server.Jobs.Maintenance;
using Server.Models;
using Server.Services;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class ClearOldCIImagesFlowTests : IDisposable
{
    private readonly XunitLogger<ClearOldCIImagesPrepareJob> loggerPrepare;
    private readonly XunitLogger<ClearOldCIImagesCleanupJob> loggerCleanup;
    private readonly XunitLogger<LockCIImageItemJob> loggerLock;

    public ClearOldCIImagesFlowTests(ITestOutputHelper output)
    {
        loggerPrepare = new XunitLogger<ClearOldCIImagesPrepareJob>(output);
        loggerCleanup = new XunitLogger<ClearOldCIImagesCleanupJob>(output);
        loggerLock = new XunitLogger<LockCIImageItemJob>(output);
    }

    [Fact]
    public async Task ClearOldCIImages_OnlyUnusedAreDeleted()
    {
        // Arrange test DB and mocks
        var notifications = Substitute.For<IModelUpdateNotificationSender>();
        var dbFixture = new Fixtures.EditableInMemoryDatabaseFixtureWithNotifications(notifications,
            nameof(ClearOldCIImages_OnlyUnusedAreDeleted));

        var db = dbFixture.NotificationsEnabledDatabase;

        var jobClient = Substitute.For<IBackgroundJobClient>();

        // Build CI/Images folder structure
        var rootCI = new StorageItem
        {
            Name = "CI", Ftype = FileType.Folder, ReadAccess = FileAccess.Public, WriteAccess = FileAccess.OwnerOrAdmin,
        };
        var imagesFolder = new StorageItem
        {
            Name = "Images", Ftype = FileType.Folder, Parent = rootCI, ReadAccess = FileAccess.Public,
            WriteAccess = FileAccess.OwnerOrAdmin,
        };

        await db.StorageItems.AddAsync(rootCI);
        await db.StorageItems.AddAsync(imagesFolder);

        // Helper to add a file with a single uploaded version
        async Task<StorageItem> CreateImageFileAsync(string name)
        {
            var fileItem = new StorageItem
            {
                Name = name,
                Ftype = FileType.File,
                Parent = imagesFolder,
                ReadAccess = FileAccess.Public,
                WriteAccess = FileAccess.Developer, // initially writable by developers
                Special = false,
            };

            await db.StorageItems.AddAsync(fileItem);
            await db.SaveChangesAsync();

            var version = await fileItem.CreateNextVersion(db, null);
            await db.StorageItemVersions.AddAsync(version);

            // Create a storage file and mark upload complete
            var storageFile = await version.CreateStorageFile(db, DateTime.UtcNow.AddMinutes(5), 123);
            storageFile.Uploading = false;
            version.Uploading = false;

            await db.SaveChangesAsync();

            return fileItem;
        }

        var image1 = await CreateImageFileAsync("image1.tar");
        var image2 = await CreateImageFileAsync("image2.tar");

        // Initially mark both images as used by CI (lock them)
        var lockJob = new LockCIImageItemJob(loggerLock, db, jobClient);

        await lockJob.Execute(image1.Id, CancellationToken.None);
        await lockJob.Execute(image2.Id, CancellationToken.None);

        await db.Entry(image1).ReloadAsync();
        await db.Entry(image2).ReloadAsync();

        Assert.Equal(FileAccess.Nobody, image1.WriteAccess);
        Assert.Equal(FileAccess.Nobody, image2.WriteAccess);
        Assert.True(image1.Special);
        Assert.True(image2.Special);

        // Act 1: run prepare job (should mark files admin-writable and bump UpdatedAt)
        var prepareJob = new ClearOldCIImagesPrepareJob(loggerPrepare, db, db, jobClient);

        var execOp = new ExecutedMaintenanceOperation("clearOldCIImages");
        await db.ExecutedMaintenanceOperations.AddAsync(execOp);
        await db.SaveChangesAsync();

        var before1 = image1.UpdatedAt;
        var before2 = image2.UpdatedAt;

        Assert.NotEqual(FileAccess.OwnerOrAdmin, image1.WriteAccess);
        Assert.NotEqual(FileAccess.OwnerOrAdmin, image2.WriteAccess);

        await prepareJob.Execute(execOp.Id, CancellationToken.None);

        await db.Entry(image1).ReloadAsync();
        await db.Entry(image2).ReloadAsync();

        Assert.Equal(FileAccess.OwnerOrAdmin, image1.WriteAccess);
        Assert.Equal(FileAccess.OwnerOrAdmin, image2.WriteAccess);
        Assert.True(image1.UpdatedAt >= before1);
        Assert.True(image2.UpdatedAt >= before2);

        // Simulate one image being used again: image1 gets locked again by CI
        // Runner schedules LockCIImageItemJob when WriteAccess != Nobody; we directly run the lock job again here
        Assert.NotEqual(FileAccess.Nobody, image1.WriteAccess);
        await lockJob.Execute(image1.Id, CancellationToken.None);
        await db.Entry(image1).ReloadAsync();
        Assert.Equal(FileAccess.Nobody, image1.WriteAccess);

        // Backdate UpdatedAt far enough for deletion checks
        // image2 will stay admin-writable and old -> candidate for deletion
        image1.UpdatedAt = DateTime.UtcNow - TimeSpan.FromDays(91);
        image2.UpdatedAt = DateTime.UtcNow - TimeSpan.FromDays(91);
        await db.SaveChangesAsync();

        // Act 2: run cleanup job
        var cleanupJob = new ClearOldCIImagesCleanupJob(loggerCleanup, db, jobClient);
        await cleanupJob.Execute(CancellationToken.None);

        // Assert: only image2 (admin-writable and older than a week) gets scheduled for deletion
        // We can't observe immediate DB deletion here (deletion is queued), so verify job scheduling via
        // IBackgroundJobClient
        jobClient.Received(1).Create(Arg.Is<Hangfire.Common.Job>(j => j != null
            && j.Type == typeof(DeleteStorageItemVersionJob)), Arg.Any<Hangfire.States.IState>());

        jobClient.Received(1).Create(Arg.Is<Hangfire.Common.Job>(j => j != null
            && j.Type == typeof(DeleteStorageItemJob)), Arg.Any<Hangfire.States.IState>());

        // TODO: maybe we should have some stronger way to check which image got deleted here?

        // Ensure image1 (re-locked) did not trigger a DeleteStorageItemJob scheduling: only one schedule total
        // (If more than one, image1 would likely also have been scheduled)

        // Additionally, verify that image1 remains in DB and image2 still present (not yet removed as deletion is
        // queued)
        var stillImage1 = await db.StorageItems.FindAsync(image1.Id);
        var stillImage2 = await db.StorageItems.FindAsync(image2.Id);

        Assert.NotNull(stillImage1);
        Assert.NotNull(stillImage2);
    }

    public void Dispose()
    {
        loggerPrepare.Dispose();
        loggerCleanup.Dispose();
        loggerLock.Dispose();
    }
}
