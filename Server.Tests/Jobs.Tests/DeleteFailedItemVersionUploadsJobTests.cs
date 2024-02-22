namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Jobs;
using Server.Models;
using Server.Services;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class DeleteFailedItemVersionUploadsJobTests : IDisposable
{
    private const string FilePath1 = "stuffPath1";
    private const string FilePath2 = "otherThing";
    private const string FilePath3 = "Thing3";
    private const string FilePath4 = "4";
    private const string FilePath5 = "AndLast";

    private static readonly TimeSpan VeryExpiredTime = AppInfo.DeleteFailedVersionUploadAfter * 2;
    private static readonly TimeSpan NotExpiredTime = AppInfo.DeleteFailedVersionUploadAfter / 2;

    private static readonly TimeSpan
        ExpiredTimespan = AppInfo.DeleteFailedVersionUploadAfter + TimeSpan.FromSeconds(10);

    private readonly XunitLogger<DeleteFailedItemVersionUploadsJob> logger;

    public DeleteFailedItemVersionUploadsJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<DeleteFailedItemVersionUploadsJob>(output);
    }

    [Fact]
    public async Task DeleteFailedItemVersionUploadsJob_DeletesRightVersions()
    {
        var storageMock = Substitute.For<IGeneralRemoteStorage>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("DeleteFailedUploadVersion").Options);

        var item = new StorageItem
        {
            Id = 12,
            Ftype = FileType.File,
        };

        var version1 = new StorageItemVersion
        {
            Version = 1,
            Deleted = false,
            Uploading = false,
            CreatedAt = DateTime.UtcNow - VeryExpiredTime,
            StorageFile = await CreateDummyStorageFile(database, FilePath1),
        };

        await database.StorageItemVersions.AddAsync(version1);
        item.StorageItemVersions.Add(version1);

        var version2 = new StorageItemVersion
        {
            Version = 2,
            Deleted = false,
            Uploading = true,
            CreatedAt = DateTime.UtcNow - VeryExpiredTime,
            StorageFile = await CreateDummyStorageFile(database, FilePath2),
        };

        await database.StorageItemVersions.AddAsync(version2);
        item.StorageItemVersions.Add(version2);

        var version3 = new StorageItemVersion
        {
            Version = 3,
            Deleted = false,
            Uploading = true,
            CreatedAt = DateTime.UtcNow - ExpiredTimespan,
            StorageFile = await CreateDummyStorageFile(database, FilePath3),
        };

        await database.StorageItemVersions.AddAsync(version3);
        item.StorageItemVersions.Add(version3);

        var version4 = new StorageItemVersion
        {
            Version = 4,
            Deleted = false,
            Uploading = true,
            CreatedAt = DateTime.UtcNow - NotExpiredTime,
            StorageFile = await CreateDummyStorageFile(database, FilePath4),
        };

        await database.StorageItemVersions.AddAsync(version4);
        item.StorageItemVersions.Add(version4);

        var version5 = new StorageItemVersion
        {
            Version = 5,
            Deleted = false,
            Uploading = true,
            StorageFile = await CreateDummyStorageFile(database, FilePath5),
        };

        await database.StorageItemVersions.AddAsync(version5);
        item.StorageItemVersions.Add(version5);

        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new DeleteFailedItemVersionUploadsJob(logger, database, storageMock);

        await job.Execute(CancellationToken.None);

        Assert.NotNull(
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.StorageItem == item && v.Version == 1));
        Assert.Null(
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.StorageItem == item && v.Version == 2));
        Assert.Null(
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.StorageItem == item && v.Version == 3));
        Assert.NotNull(
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.StorageItem == item && v.Version == 4));
        Assert.NotNull(
            await database.StorageItemVersions.FirstOrDefaultAsync(v => v.StorageItem == item && v.Version == 5));

        Assert.False(version1.Deleted);
        Assert.True(version2.Deleted);
        Assert.True(version3.Deleted);
        Assert.False(version4.Deleted);
        Assert.False(version5.Deleted);

        await storageMock.Received().DeleteObject(FilePath2);
        await storageMock.Received().DeleteObject($"@upload/{FilePath2}");
        await storageMock.Received().DeleteObject(FilePath3);
        await storageMock.Received().DeleteObject($"@upload/{FilePath3}");

        // Check no other delete calls
        Assert.Equal(4, storageMock.ReceivedCalls().Count());
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static async Task<StorageFile> CreateDummyStorageFile(ApplicationDbContext database, string path)
    {
        var versionFile = new StorageFile
        {
            StoragePath = path,
        };

        await database.StorageFiles.AddAsync(versionFile);

        return versionFile;
    }
}
