namespace ThriveDevCenter.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Controllers;
using Server.Models;
using Server.Services;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class StorageFileVersionTests : IDisposable
{
    private const long DeletedFile1 = 676008;
    private const long DeletedFile2 = 676009;

    private const long File1Id = 676010;
    private const long File2Id = 676011;
    private const long File3Id = 676012;
    private const long File4Id = 676013;

    private static readonly Mock<IModelUpdateNotificationSender> ReadonlyDbNotifications = new();

    private static readonly Lazy<NotificationsEnabledDb> NonWritingTestDb = new(CreateReadOnlyDatabase);

    private static readonly User DeveloperUser = UserUtilities.CreateDeveloperUser(100);
    private static readonly User NormalUser = UserUtilities.CreateNormalUser(101);
    private static readonly User AdminUser = UserUtilities.CreateAdminUser(102);
    private static readonly User RandomNormalUser = UserUtilities.CreateNormalUser(103);

    private readonly XunitLogger<StorageFilesController> logger;

    public StorageFileVersionTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<StorageFilesController>(output);
    }

    [Fact]
    public async Task StorageFilesController_CannotRestoreDeletedVersionInDeletedFile()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(NormalUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.RestoreVersion(DeletedFile1, 1);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);
        Assert.Contains("in a deleted item", Assert.IsType<string>(response.Value));

        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == DeletedFile1);
        Assert.NotNull(item);

        var version1 = item.StorageItemVersions.First(v => v.Version == 1);

        Assert.True(version1.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_CannotDeleteUploadingVersion()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.MarkVersionAsDeleted(File3Id, 3);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);
        Assert.Contains("an uploading version", Assert.IsType<string>(response.Value));

        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == File3Id);
        Assert.NotNull(item);

        var version3 = item.StorageItemVersions.First(v => v.Version == 3);

        Assert.False(version3.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_CannotDeleteTheLastVersionInFile()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.MarkVersionAsDeleted(File3Id, 2);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);
        Assert.Contains("last version", Assert.IsType<string>(response.Value));

        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == File3Id);
        Assert.NotNull(item);

        var version3 = item.StorageItemVersions.First(v => v.Version == 2);

        Assert.False(version3.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_OthersCannotSetImportantStatus()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.MarkImportant(File4Id);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);
        Assert.Contains("Only item owner", Assert.IsType<string>(response.Value));

        var item = await database.StorageItems.FindAsync(File4Id);
        Assert.NotNull(item);
        Assert.False(item.Important);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_RestoringFileVersionWorks()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_RestoringFileVersionWorks));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.RestoreVersion(File2Id, 1);

        Assert.IsType<OkResult>(result);

        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == File2Id);
        Assert.NotNull(item);
        Assert.NotEmpty(item.StorageItemVersions);

        var version1 = item.StorageItemVersions.First(v => v.Version == 1);
        var version2 = item.StorageItemVersions.First(v => v.Version == 2);
        var version3 = item.StorageItemVersions.First(v => v.Version == 3);

        Assert.False(version1.Deleted);
        Assert.False(version2.Deleted);
        Assert.True(version3.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_CanDeleteVersionAndRestore()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_CanDeleteVersionAndRestore));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(NormalUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.MarkVersionAsDeleted(File4Id, 2);

        Assert.IsType<OkResult>(result);

        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == File4Id);
        Assert.NotNull(item);
        Assert.NotEmpty(item.StorageItemVersions);

        var version1 = item.StorageItemVersions.First(v => v.Version == 1);
        var version2 = item.StorageItemVersions.First(v => v.Version == 2);

        Assert.False(version1.Deleted);
        Assert.True(version2.Deleted);

        result = await controller.RestoreVersion(File4Id, 2);

        Assert.IsType<OkResult>(result);

        Assert.False(version1.Deleted);
        Assert.False(version2.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_DeletingVersionInImportantItemFails()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_DeletingVersionInImportantItemFails));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(NormalUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.MarkImportant(File4Id);

        Assert.IsType<OkResult>(result);

        result = await controller.MarkVersionAsDeleted(File4Id, 2);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);
        Assert.Contains("This item or version", Assert.IsType<string>(response.Value));
        Assert.Contains("important", Assert.IsType<string>(response.Value));

        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == File4Id);
        Assert.NotNull(item);

        var version2 = item.StorageItemVersions.First(v => v.Version == 2);

        Assert.False(version2.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_SettingVersionAsKeepPreventsDeleting()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_SettingVersionAsKeepPreventsDeleting));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.MarkVersionKeep(File4Id, 2);

        Assert.IsType<OkResult>(result);

        result = await controller.MarkVersionAsDeleted(File4Id, 2);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);
        Assert.Contains("versions marked as keep", Assert.IsType<string>(response.Value));

        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == File4Id);
        Assert.NotNull(item);

        var version2 = item.StorageItemVersions.First(v => v.Version == 2);

        Assert.False(version2.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_OwnerCanDeleteKeptVersion()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_OwnerCanDeleteKeptVersion));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(NormalUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.MarkVersionKeep(File4Id, 2);

        Assert.IsType<OkResult>(result);

        result = await controller.MarkVersionAsDeleted(File4Id, 2);

        Assert.IsType<OkResult>(result);

        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == File4Id);
        Assert.NotNull(item);

        var version2 = item.StorageItemVersions.First(v => v.Version == 2);

        Assert.True(version2.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static NotificationsEnabledDb CreateReadOnlyDatabase()
    {
        var database = new NotificationsEnabledDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("StorageFileVersionTestDB")
                .Options, ReadonlyDbNotifications.Object);
        CreateDefaultItems(database).Wait();
        return database;
    }

    private static async Task<NotificationsEnabledDb> GetWritableDatabase(string testName)
    {
        var notificationsMock = new Mock<IModelUpdateNotificationSender>();

        var database = new NotificationsEnabledDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(testName).Options,
            notificationsMock.Object);
        await CreateDefaultItems(database);
        return database;
    }

    private static async Task CreateDefaultItems(ApplicationDbContext database)
    {
        var random = new Random(1223456);

        // Setup the starting item and version data
        var file1 = new StorageItem
        {
            Id = File1Id,
            Name = "File1",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = NormalUser.Id,
        };

        var file1Version1 = new StorageItemVersion
        {
            Version = 1,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            UploadedById = RandomNormalUser.Id,
        };

        file1.StorageItemVersions.Add(file1Version1);

        await database.StorageItemVersions.AddAsync(file1Version1);
        await database.StorageItems.AddAsync(file1);

        var file2 = new StorageItem
        {
            Id = File2Id,
            Name = "File2",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = NormalUser.Id,
        };

        var file2Version1 = new StorageItemVersion
        {
            Version = 1,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            Deleted = true,
            UploadedById = RandomNormalUser.Id,
        };

        var file2Version2 = new StorageItemVersion
        {
            Version = 2,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            Deleted = false,
            UploadedById = RandomNormalUser.Id,
        };

        var file2Version3 = new StorageItemVersion
        {
            Version = 3,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            Deleted = true,
            UploadedById = RandomNormalUser.Id,
        };

        file2.StorageItemVersions.Add(file2Version1);
        file2.StorageItemVersions.Add(file2Version2);
        file2.StorageItemVersions.Add(file2Version3);

        await database.StorageItemVersions.AddAsync(file2Version1);
        await database.StorageItemVersions.AddAsync(file2Version2);
        await database.StorageItemVersions.AddAsync(file2Version3);
        await database.StorageItems.AddAsync(file2);

        var file3 = new StorageItem
        {
            Id = File3Id,
            Name = "file3",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = DeveloperUser.Id,
        };

        var file3Version1 = new StorageItemVersion
        {
            Version = 1,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            Deleted = true,
            UploadedById = DeveloperUser.Id,
        };

        var file3Version2 = new StorageItemVersion
        {
            Version = 2,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            Deleted = false,
            UploadedById = DeveloperUser.Id,
        };

        var file3Version3 = new StorageItemVersion
        {
            Version = 3,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = true,
            Deleted = false,
            UploadedById = DeveloperUser.Id,
        };

        file3.StorageItemVersions.Add(file3Version1);
        file3.StorageItemVersions.Add(file3Version2);
        file3.StorageItemVersions.Add(file3Version3);

        await database.StorageItemVersions.AddAsync(file3Version1);
        await database.StorageItemVersions.AddAsync(file3Version2);
        await database.StorageItemVersions.AddAsync(file3Version3);
        await database.StorageItems.AddAsync(file3);

        var file4 = new StorageItem
        {
            Id = File4Id,
            Name = "file4",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = NormalUser.Id,
        };

        var file4Version1 = new StorageItemVersion
        {
            Version = 1,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            Deleted = false,
            UploadedById = NormalUser.Id,
        };

        var file4Version2 = new StorageItemVersion
        {
            Version = 2,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            Deleted = false,
            UploadedById = NormalUser.Id,
        };

        file4.StorageItemVersions.Add(file4Version1);
        file4.StorageItemVersions.Add(file4Version2);

        await database.StorageItemVersions.AddAsync(file4Version1);
        await database.StorageItemVersions.AddAsync(file4Version2);
        await database.StorageItems.AddAsync(file4);

        // Setup deleted items
        int trashId = 431;

        await TestFileUtilities.CreateTrashFolder(database, trashId);

        var deleted1 = new StorageItem
        {
            Id = DeletedFile1,
            Name = "Name1",
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.RestrictedUser,
            OwnerId = NormalUser.Id,
            Deleted = true,
        };

        var deleted1Version1 = new StorageItemVersion
        {
            Version = 1,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            Deleted = true,
            UploadedById = RandomNormalUser.Id,
        };

        var deleted1Version2 = new StorageItemVersion
        {
            Version = 2,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            UploadedById = RandomNormalUser.Id,
        };

        deleted1.StorageItemVersions.Add(deleted1Version1);
        deleted1.StorageItemVersions.Add(deleted1Version2);

        await database.StorageItemVersions.AddAsync(deleted1Version1);
        await database.StorageItemVersions.AddAsync(deleted1Version2);
        await database.StorageItems.AddAsync(deleted1);

        var deleted2 = new StorageItem
        {
            Id = DeletedFile2,
            Name = "File1",
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.Nobody,
            OwnerId = DeveloperUser.Id,
            Deleted = true,
        };

        var deleted2Version1 = new StorageItemVersion
        {
            Version = 1,
            StorageFile = await CreateDummyStorageFile(database, random),
            Uploading = false,
            UploadedById = NormalUser.Id,
        };

        deleted2.StorageItemVersions.Add(deleted2Version1);

        await database.StorageItemVersions.AddAsync(deleted2Version1);
        await database.StorageItems.AddAsync(deleted2);

        await database.SaveChangesAsync();
    }

    private static async Task<StorageFile> CreateDummyStorageFile(ApplicationDbContext database, Random random)
    {
        var versionFile = new StorageFile
        {
            StoragePath = random.Next().ToString(),
            Size = 123,
        };

        await database.StorageFiles.AddAsync(versionFile);

        return versionFile;
    }
}
