namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class PurgeOldDeletedFileVersionsJobTests : IDisposable
{
    private static readonly TimeSpan OldVersion = AppInfo.DeleteFileVersionsAfter * 2;
    private static readonly TimeSpan NotOldVersionTime = AppInfo.DeleteFileVersionsAfter / 2;
    private static readonly TimeSpan AlmostOldVersion = AppInfo.DeleteFileVersionsAfter - TimeSpan.FromSeconds(10);

    private readonly XunitLogger<PurgeOldDeletedFileVersionsJob> logger;

    private readonly Random random = new();

    public PurgeOldDeletedFileVersionsJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<PurgeOldDeletedFileVersionsJob>(output);
    }

    [Fact]
    public async Task PurgeOldDeletedFileVersionsJob_NewOrUndeletedVersionsAreNotDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFileVersionsJob_NewOrUndeletedVersionsAreNotDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = false,
            Deleted = false,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        var version2 = new StorageItemVersion
        {
            Version = 2,
            Uploading = false,
            Deleted = true,
            UpdatedAt = DateTime.UtcNow - NotOldVersionTime,
            StorageFile = await CreateDummyStorageFile(database),
        };

        await database.StorageItemVersions.AddAsync(version2);
        item.StorageItemVersions.Add(version2);

        var version3 = new StorageItemVersion
        {
            Version = 3,
            Uploading = false,
            Deleted = false,
            UpdatedAt = DateTime.UtcNow,
            StorageFile = await CreateDummyStorageFile(database),
        };

        await database.StorageItemVersions.AddAsync(version3);
        item.StorageItemVersions.Add(version3);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFileVersionsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        // This test relies on this to detect if something was deleted or not
        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task PurgeOldDeletedFileVersionsJob_OldVersionIsDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFileVersionsJob_OldVersionIsDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = false,
            Deleted = true,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFileVersionsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        jobClientMock.Received().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task PurgeOldDeletedFileVersionsJob_UploadingIsNotDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFileVersionsJob_UploadingIsNotDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = true,
            Deleted = true,
            UpdatedAt = DateTime.UtcNow - OldVersion,
            StorageFile = await CreateDummyStorageFile(database),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFileVersionsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task PurgeOldDeletedFileVersionsJob_RecentDeletedIsNotDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFileVersionsJob_RecentDeletedIsNotDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Uploading = true,
            Deleted = true,
            UpdatedAt = DateTime.UtcNow - AlmostOldVersion,
            StorageFile = await CreateDummyStorageFile(database),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        var version2 = new StorageItemVersion
        {
            Version = 2,
            Uploading = false,
            Deleted = true,
            UpdatedAt = DateTime.UtcNow - NotOldVersionTime,
            StorageFile = await CreateDummyStorageFile(database),
        };

        await database.StorageItemVersions.AddAsync(version2);
        item.StorageItemVersions.Add(version2);

        var version3 = new StorageItemVersion
        {
            Version = 3,
            Uploading = false,
            Deleted = true,
            UpdatedAt = DateTime.UtcNow,
            StorageFile = await CreateDummyStorageFile(database),
        };

        await database.StorageItemVersions.AddAsync(version3);
        item.StorageItemVersions.Add(version3);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFileVersionsJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private async Task<StorageFile> CreateDummyStorageFile(ApplicationDbContext database)
    {
        var versionFile = new StorageFile
        {
            StoragePath = random.Next().ToString(),
        };

        await database.StorageFiles.AddAsync(versionFile);

        return versionFile;
    }
}
