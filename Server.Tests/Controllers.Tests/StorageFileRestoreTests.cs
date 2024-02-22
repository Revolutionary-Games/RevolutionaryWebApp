namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Net;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Controllers;
using Server.Filters;
using Server.Models;
using Server.Services;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class StorageFileRestoreTests : IDisposable
{
    private const long TestParentFolderId = 676000;
    private const long EmptyFolderId = 676001;
    private const long NonEmptyFolderId = 676002;
    private const long SubFolderId = 676003;

    private const long FileInRootId = 676004;
    private const long DeveloperOnlyFolderId = 676005;

    private const long SpecialFolderId = 676007;

    private const long FileToRestoreId1 = 676008;
    private const long FileToRestoreId2 = 676009;
    private const long FileToRestoreId3 = 676010;
    private const long FileToRestoreId4 = 676011;
    private const long FileToRestoreId5 = 676012;
    private const long FileToRestoreId6 = 676013;
    private const string FileToRestore6ChangedName = "A file";
    private const string FileToRestore6OriginalName = "A different name";
    private const long FileToRestoreId7 = 676014;

    private static readonly IModelUpdateNotificationSender ReadonlyDbNotifications =
        Substitute.For<IModelUpdateNotificationSender>();

    private static readonly Lazy<NotificationsEnabledDb> NonWritingTestDb = new(CreateReadOnlyDatabase);

    private static readonly User DeveloperUser = UserUtilities.CreateDeveloperUser(100);
    private static readonly User NormalUser = UserUtilities.CreateNormalUser(101);
    private static readonly User AdminUser = UserUtilities.CreateAdminUser(102);
    private static readonly User RandomNormalUser = UserUtilities.CreateNormalUser(103);

    private readonly XunitLogger<StorageFilesController> logger;

    public StorageFileRestoreTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<StorageFilesController>(output);
    }

    [Fact]
    public async Task StorageFilesController_CannotRestoreFileOverExistingOne()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result =
            await Assert.ThrowsAsync<HttpResponseException>(() => controller.RestoreFile(FileToRestoreId1, null));

        Assert.Equal((int)HttpStatusCode.Conflict, result.Status);
        Assert.Contains("has an item named", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToRestoreId1);
        Assert.NotNull(item);
        Assert.True(item.Deleted);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_NonAdminCannotRestoreToRootFolder()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result =
            await Assert.ThrowsAsync<HttpResponseException>(() => controller.RestoreFile(FileToRestoreId2, null));

        Assert.Equal((int)HttpStatusCode.Conflict, result.Status);
        Assert.Contains("admins can write", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToRestoreId2);
        Assert.NotNull(item);
        Assert.True(item.Deleted);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_JustSlashesPathResultsInRootFolder()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result =
            await Assert.ThrowsAsync<HttpResponseException>(() => controller.RestoreFile(FileToRestoreId2, "////"));

        Assert.Equal((int)HttpStatusCode.Conflict, result.Status);
        Assert.Contains("admins can write", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToRestoreId2);
        Assert.NotNull(item);
        Assert.True(item.Deleted);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CannotRestoreToFolderWithoutWriteAccess()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(NormalUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result =
            await Assert.ThrowsAsync<HttpResponseException>(() => controller.RestoreFile(FileToRestoreId3, null));

        Assert.Equal((int)HttpStatusCode.Conflict, result.Status);
        Assert.Contains("do not have write access", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToRestoreId3);
        Assert.NotNull(item);
        Assert.True(item.Deleted);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    // We can't test for non-logged in not being able to restore because the whole endpoint disallows calls by
    // non-logged in clients

    [Fact]
    public async Task StorageFilesController_NoPermissionUserCannotRestore()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(RandomNormalUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result =
            await Assert.ThrowsAsync<HttpResponseException>(() => controller.RestoreFile(FileToRestoreId4, null));

        Assert.Equal((int)HttpStatusCode.Forbidden, result.Status);
        Assert.Contains("permissions required to restore", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToRestoreId4);
        Assert.NotNull(item);
        Assert.True(item.Deleted);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CantCreateMissingFolderWithoutWriteAccess()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(RandomNormalUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result =
            await Assert.ThrowsAsync<HttpResponseException>(() => controller.RestoreFile(FileToRestoreId7, null));

        Assert.Equal((int)HttpStatusCode.Forbidden, result.Status);
        Assert.Contains("permissions required to restore", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToRestoreId7);
        Assert.NotNull(item);
        Assert.True(item.Deleted);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CanRestoreFileByOwner()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_CanRestoreFileByOwner));
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result = await controller.RestoreFile(FileToRestoreId4, null);

        var resultPath = Assert.IsType<string>(Assert.IsAssignableFrom<OkObjectResult>(result).Value);

        Assert.Equal("StorageFileRestoreTestParent/EmptyFolder/Just a random file", resultPath);

        var item = await database.StorageItems.FindAsync(FileToRestoreId4);
        Assert.NotNull(item);
        Assert.False(item.Deleted);
        Assert.Equal(EmptyFolderId, item.ParentId);
        Assert.Equal(FileAccess.User, item.WriteAccess);

        var deleteInfo = await database.StorageItemDeleteInfos.FindAsync(FileToRestoreId4);
        Assert.Null(deleteInfo);

        // We don't check the jobs mock for no other calls as a folder item recount should have been triggered
    }

    [Fact]
    public async Task StorageFilesController_CanRestoreFileByDeleter()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_CanRestoreFileByDeleter));
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(NormalUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result = await controller.RestoreFile(FileToRestoreId4, null);

        var resultPath = Assert.IsType<string>(Assert.IsAssignableFrom<OkObjectResult>(result).Value);

        Assert.Equal("StorageFileRestoreTestParent/EmptyFolder/Just a random file", resultPath);

        var item = await database.StorageItems.FindAsync(FileToRestoreId4);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        Assert.Null(await database.StorageItemDeleteInfos.FindAsync(FileToRestoreId4));
    }

    [Fact]
    public async Task StorageFilesController_AdminCanRestoreToRoot()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_AdminCanRestoreToRoot));
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(AdminUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result = await controller.RestoreFile(FileToRestoreId2, null);

        var resultPath = Assert.IsType<string>(Assert.IsAssignableFrom<OkObjectResult>(result).Value);

        Assert.Equal("/File1", resultPath);

        var item = await database.StorageItems.FindAsync(FileToRestoreId2);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        Assert.Null(await database.StorageItemDeleteInfos.FindAsync(FileToRestoreId2));
    }

    [Fact]
    public async Task StorageFilesController_RestoreCanCreateMissingFolder()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_RestoreCanCreateMissingFolder));
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result = await controller.RestoreFile(FileToRestoreId5, null);

        var resultPath = Assert.IsType<string>(Assert.IsAssignableFrom<OkObjectResult>(result).Value);

        Assert.Equal("StorageFileRestoreTestParent/CreateFolder/File 3", resultPath);

        var item = await database.StorageItems.FindAsync(FileToRestoreId5);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        Assert.Null(await database.StorageItemDeleteInfos.FindAsync(FileToRestoreId5));

        // Verify the actual path matches what the API said to ensure there isn't an internal problem
        Assert.Equal(await item.ComputeStoragePath(database), resultPath);
    }

    [Fact]
    public async Task StorageFilesController_RestoreRestoresOriginalName()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_RestoreRestoresOriginalName));
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result = await controller.RestoreFile(FileToRestoreId6, null);

        var item = await database.StorageItems.FindAsync(FileToRestoreId6);
        Assert.NotNull(item);
        Assert.Equal(FileToRestore6OriginalName, item.Name);

        // Fix up the in-memory object back to what it should be
        item.Name = FileToRestore6ChangedName;

        Assert.False(item.Deleted);

        var resultPath = Assert.IsType<string>(Assert.IsAssignableFrom<OkObjectResult>(result).Value);

        Assert.Equal($"StorageFileRestoreTestParent/{FileToRestore6OriginalName}", resultPath);

        Assert.Null(await database.StorageItemDeleteInfos.FindAsync(FileToRestoreId6));
    }

    [Fact]
    public async Task StorageFilesController_CustomRestorePathWorks()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_CustomRestorePathWorks));
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result = await controller.RestoreFile(FileToRestoreId2, "StorageFileRestoreTestParent/EmptyFolder");

        var resultPath = Assert.IsType<string>(Assert.IsAssignableFrom<OkObjectResult>(result).Value);

        Assert.Equal("StorageFileRestoreTestParent/EmptyFolder/File1", resultPath);

        var item = await database.StorageItems.FindAsync(FileToRestoreId2);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        Assert.Null(await database.StorageItemDeleteInfos.FindAsync(FileToRestoreId2));

        Assert.Equal(await item.ComputeStoragePath(database), resultPath);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static NotificationsEnabledDb CreateReadOnlyDatabase()
    {
        var database = new NotificationsEnabledDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("StorageFileRestoreTestsRO")
                .Options, ReadonlyDbNotifications);
        CreateDefaultItems(database).Wait();
        return database;
    }

    private static async Task<NotificationsEnabledDb> GetWritableDatabase(string testName)
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        var database = new NotificationsEnabledDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(testName).Options,
            notificationsMock);
        await CreateDefaultItems(database);
        return database;
    }

    private static async Task CreateDefaultItems(ApplicationDbContext database)
    {
        // Setup where things will be restored to
        var parentFolder = new StorageItem
        {
            Id = TestParentFolderId,
            Name = "StorageFileRestoreTestParent",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        };

        await database.StorageItems.AddAsync(parentFolder);

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = EmptyFolderId,
            Name = "EmptyFolder",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        });

        var nonEmptyFolder = new StorageItem
        {
            Id = NonEmptyFolderId,
            Name = "NonEmpty",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        };

        await database.StorageItems.AddAsync(nonEmptyFolder);

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = SubFolderId,
            Name = "Name1",
            Ftype = FileType.Folder,
            Parent = nonEmptyFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FileInRootId,
            Name = "ItemInRoot",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = DeveloperOnlyFolderId,
            Name = "DevOnly",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Developer,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = SpecialFolderId,
            Name = "Special",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Nobody,
            Special = true,
        });

        // Setup deleted items
        int trashId = 431;

        await TestFileUtilities.CreateTrashFolder(database, trashId);

        var deleted1 = new StorageItem
        {
            Id = FileToRestoreId1,
            Name = "Name1",
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.Nobody,
            OwnerId = DeveloperUser.Id,
            Deleted = true,
        };

        await database.StorageItems.AddAsync(deleted1);

        var deleted2 = new StorageItem
        {
            Id = FileToRestoreId2,
            Name = "File1",
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.Nobody,
            OwnerId = DeveloperUser.Id,
            Deleted = true,
        };

        await database.StorageItems.AddAsync(deleted2);

        var deleted3 = new StorageItem
        {
            Id = FileToRestoreId3,
            Name = "File2",
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.Nobody,
            OwnerId = NormalUser.Id,
            Deleted = true,
        };

        await database.StorageItems.AddAsync(deleted3);

        var deleted4 = new StorageItem
        {
            Id = FileToRestoreId4,
            Name = "Just a random file",
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.Nobody,
            OwnerId = DeveloperUser.Id,
            Deleted = true,
        };

        await database.StorageItems.AddAsync(deleted4);

        var deleted5 = new StorageItem
        {
            Id = FileToRestoreId5,
            Name = "File 3",
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.Nobody,
            OwnerId = DeveloperUser.Id,
            Deleted = true,
        };

        await database.StorageItems.AddAsync(deleted5);

        var deleted6 = new StorageItem
        {
            Id = FileToRestoreId6,
            Name = FileToRestore6ChangedName,
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.Nobody,
            OwnerId = DeveloperUser.Id,
            Deleted = true,
        };

        await database.StorageItems.AddAsync(deleted6);

        var deleted7 = new StorageItem
        {
            Id = FileToRestoreId7,
            Name = "Some name",
            Ftype = FileType.File,
            ParentId = trashId,
            ReadAccess = FileAccess.OwnerOrAdmin,
            WriteAccess = FileAccess.Nobody,
            OwnerId = NormalUser.Id,
            Deleted = true,
        };

        await database.StorageItems.AddAsync(deleted7);

        await database.SaveChangesAsync();

        // Setup the deleted data
        await database.StorageItemDeleteInfos.AddAsync(new StorageItemDeleteInfo(deleted1.Id, FileAccess.User,
            FileAccess.User, "StorageFileRestoreTestParent/NonEmpty/Name1"));

        await database.StorageItemDeleteInfos.AddAsync(new StorageItemDeleteInfo(deleted2.Id, FileAccess.User,
            FileAccess.User, "File1"));

        await database.StorageItemDeleteInfos.AddAsync(new StorageItemDeleteInfo(deleted3.Id, FileAccess.User,
            FileAccess.User, "StorageFileRestoreTestParent/DevOnly/File2"));

        await database.StorageItemDeleteInfos.AddAsync(new StorageItemDeleteInfo(deleted4.Id, FileAccess.User,
            FileAccess.User, "StorageFileRestoreTestParent/EmptyFolder/Just a random file")
        {
            DeletedById = NormalUser.Id,
        });

        await database.StorageItemDeleteInfos.AddAsync(new StorageItemDeleteInfo(deleted5.Id, FileAccess.User,
            FileAccess.User, "StorageFileRestoreTestParent/CreateFolder/File 3"));

        await database.StorageItemDeleteInfos.AddAsync(new StorageItemDeleteInfo(deleted6.Id, FileAccess.User,
            FileAccess.User, $"StorageFileRestoreTestParent/{FileToRestore6OriginalName}"));

        await database.StorageItemDeleteInfos.AddAsync(new StorageItemDeleteInfo(deleted7.Id, FileAccess.User,
            FileAccess.User, $"StorageFileRestoreTestParent/DevOnly/Some name"));

        await database.SaveChangesAsync();
    }
}
