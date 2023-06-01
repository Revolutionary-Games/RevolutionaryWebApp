namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class CleanOldFileVersionsJobTests : IDisposable
{
    private static readonly TimeSpan OldVersion = AppInfo.DeleteFileVersionsAfter * 2;
    private static readonly TimeSpan NotOldVersionTime = AppInfo.DeleteFileVersionsAfter / 2;

    private readonly XunitLogger<CleanOldFileVersionsJob> logger;

    private readonly Random random = new();

    public CleanOldFileVersionsJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<CleanOldFileVersionsJob>(output);
    }

    [Fact]
    public async Task CleanOldFileVersionsJob_DeletesRightVersionsInLargeFile()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();
        jobClientMock.Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>())).Verifiable();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(CleanOldFileVersionsJob_DeletesRightVersionsInLargeFile)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
            Size = AppInfo.LargeFileSizeVersionsKeepLimit + 1,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - OldVersion - TimeSpan.FromSeconds(10),
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        var version2 = new StorageItemVersion
        {
            Version = 2,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - OldVersion - TimeSpan.FromSeconds(5),
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version2);
        item.StorageItemVersions.Add(version2);

        var version3 = new StorageItemVersion
        {
            Version = 3,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version3);
        item.StorageItemVersions.Add(version3);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new CleanOldFileVersionsJob(logger, database, jobClientMock.Object);

        await job.Execute(CancellationToken.None);

        Assert.True(version1.Deleted);
        Assert.True(version2.Deleted);
        Assert.False(version3.Deleted);

        jobClientMock.Verify();
    }

    [Fact]
    public async Task CleanOldFileVersionsJob_UploadingVersionsAreNotDeleted()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();
        jobClientMock.Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>())).Verifiable();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(CleanOldFileVersionsJob_UploadingVersionsAreNotDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
            Size = AppInfo.LargeFileSizeVersionsKeepLimit + 1,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        var version2 = new StorageItemVersion
        {
            Version = 2,
            Uploading = true,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version2);
        item.StorageItemVersions.Add(version2);

        var version3 = new StorageItemVersion
        {
            Version = 3,
            Uploading = true,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version3);
        item.StorageItemVersions.Add(version3);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new CleanOldFileVersionsJob(logger, database, jobClientMock.Object);

        await job.Execute(CancellationToken.None);

        Assert.True(version1.Deleted);
        Assert.False(version2.Deleted);
        Assert.False(version3.Deleted);

        jobClientMock.Verify();
    }

    [Fact]
    public async Task CleanOldFileVersionsJob_NewItemsAreNotDeleted()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(CleanOldFileVersionsJob_NewItemsAreNotDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
            Size = AppInfo.LargeFileSizeVersionsKeepLimit + 1,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - NotOldVersionTime,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        var version2 = new StorageItemVersion
        {
            Version = 2,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - NotOldVersionTime,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version2);
        item.StorageItemVersions.Add(version2);

        var version3 = new StorageItemVersion
        {
            Version = 3,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - NotOldVersionTime,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.LargeFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version3);
        item.StorageItemVersions.Add(version3);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new CleanOldFileVersionsJob(logger, database, jobClientMock.Object);

        await job.Execute(CancellationToken.None);

        Assert.False(version1.Deleted);
        Assert.False(version2.Deleted);
        Assert.False(version3.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CleanOldFileVersionsJob_DeletesRightVersionsInSmallFile()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();
        jobClientMock.Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>())).Verifiable();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(CleanOldFileVersionsJob_DeletesRightVersionsInSmallFile)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
            Size = AppInfo.SmallFileSizeVersionsKeepLimit + 1,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.SmallFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        var version2 = new StorageItemVersion
        {
            Version = 2,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.SmallFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version2);
        item.StorageItemVersions.Add(version2);

        var version3 = new StorageItemVersion
        {
            Version = 3,
            Deleted = false,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.SmallFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version3);
        item.StorageItemVersions.Add(version3);

        var version4 = new StorageItemVersion
        {
            Version = 4,
            Deleted = false,
            Uploading = false,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.SmallFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version4);
        item.StorageItemVersions.Add(version4);

        var version5 = new StorageItemVersion
        {
            Version = 5,
            Deleted = false,
            Uploading = false,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.SmallFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version5);
        item.StorageItemVersions.Add(version5);

        var version6 = new StorageItemVersion
        {
            Version = 6,
            Deleted = false,
            Uploading = false,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.SmallFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version6);
        item.StorageItemVersions.Add(version6);

        var version7 = new StorageItemVersion
        {
            Version = 7,
            Deleted = false,
            Uploading = false,
            StorageFile = await CreateDummyStorageFile(database, AppInfo.SmallFileSizeVersionsKeepLimit + 1),
        };

        await database.StorageItemVersions.AddAsync(version7);
        item.StorageItemVersions.Add(version7);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new CleanOldFileVersionsJob(logger, database, jobClientMock.Object);

        await job.Execute(CancellationToken.None);

        Assert.True(version1.Deleted);
        Assert.False(version2.Deleted);
        Assert.False(version3.Deleted);
        Assert.False(version4.Deleted);
        Assert.False(version5.Deleted);
        Assert.False(version6.Deleted);

        jobClientMock.Verify();

        database.StorageItemVersions.Remove(version1);
        await database.SaveChangesAsync();

        // Running again won't delete anything new
        await job.Execute(CancellationToken.None);

        Assert.True(version1.Deleted);
        Assert.False(version2.Deleted);
        Assert.False(version3.Deleted);
        Assert.False(version4.Deleted);
        Assert.False(version5.Deleted);
        Assert.False(version6.Deleted);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private async Task<StorageFile> CreateDummyStorageFile(ApplicationDbContext database, long size)
    {
        var versionFile = new StorageFile
        {
            StoragePath = random.Next().ToString(),
            Size = size,
        };

        await database.StorageFiles.AddAsync(versionFile);

        return versionFile;
    }
}
