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

public sealed class StorageFileMoveTests : IDisposable
{
    private const long TestParentFolderId = 676000;
    private const long EmptyFolderId = 676001;
    private const long NonEmptyFolderId = 676002;
    private const long SubFolderId = 676003;

    private const long FileInRootId = 676004;
    private const long DeveloperOnlyFolderId = 676005;

    private const long FileToMove1Id = 676014;
    private const long FileToMove2Id = 676015;
    private const long FileToMove3Id = 676016;
    private const long FileToMove4Id = 676017;
    private const long FileToMove5Id = 676018;

    private const long SpecialFolderId = 676007;

    private const long FileToRestoreId1 = 676008;

    private static readonly IModelUpdateNotificationSender ReadonlyDbNotifications =
        Substitute.For<IModelUpdateNotificationSender>();

    private static readonly Lazy<NotificationsEnabledDb> NonWritingTestDb = new(CreateReadOnlyDatabase);

    private static readonly User DeveloperUser = UserUtilities.CreateDeveloperUser(100);
    private static readonly User NormalUser = UserUtilities.CreateNormalUser(101);
    private static readonly User AdminUser = UserUtilities.CreateAdminUser(102);
    private static readonly User RandomNormalUser = UserUtilities.CreateNormalUser(103);

    private readonly XunitLogger<StorageFilesController> logger;

    public StorageFileMoveTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<StorageFilesController>(output);
    }

    [Fact]
    public async Task StorageFilesController_CannotMoveFileOverExistingOne()
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

        var result = await Assert.ThrowsAsync<HttpResponseException>(() =>
            controller.MoveItem(FileToMove1Id, "StorageFileMoveTestParent/NonEmpty"));

        Assert.Equal((int)HttpStatusCode.Conflict, result.Status);
        Assert.Contains("has an item named", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToMove1Id);
        Assert.NotNull(item);
        Assert.Null(item.MovedFromLocation);
        Assert.Null(item.LastModifiedById);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_NonAdminCannotMoveToRootFolder()
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
            await Assert.ThrowsAsync<HttpResponseException>(() => controller.MoveItem(FileToMove1Id, null));

        Assert.Equal((int)HttpStatusCode.Conflict, result.Status);
        Assert.Contains("admins can write", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToMove1Id);
        Assert.NotNull(item);
        Assert.Null(item.MovedFromLocation);
        Assert.Null(item.LastModifiedById);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_NonAdminCannotMoveRootItem()
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

        var result = await Assert.ThrowsAsync<HttpResponseException>(() =>
            controller.MoveItem(FileInRootId, "StorageFileMoveTestParent/EmptyFolder"));

        Assert.Equal((int)HttpStatusCode.Forbidden, result.Status);
        Assert.Contains("without being an admin", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileInRootId);
        Assert.NotNull(item);
        Assert.Null(item.MovedFromLocation);
        Assert.Null(item.LastModifiedById);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CannotMoveWithPathEndingToAFile()
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
            await Assert.ThrowsAsync<HttpResponseException>(() =>
                controller.MoveItem(FileToMove2Id, "StorageFileMoveTestParent/Name1"));

        Assert.Equal((int)HttpStatusCode.Conflict, result.Status);
        Assert.Contains("leads to a file", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToMove2Id);
        Assert.NotNull(item);
        Assert.Null(item.MovedFromLocation);
        Assert.Null(item.LastModifiedById);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CannotMoveToFolderWithoutWriteAccess()
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
            await Assert.ThrowsAsync<HttpResponseException>(() =>
                controller.MoveItem(FileToMove1Id, "StorageFileMoveTestParent/NonEmpty/DevOnly"));

        Assert.Equal((int)HttpStatusCode.Conflict, result.Status);
        Assert.Contains("even read access", Assert.IsType<string>(result.Value));

        var item = await database.StorageItems.FindAsync(FileToMove1Id);
        Assert.NotNull(item);
        Assert.Null(item.MovedFromLocation);
        Assert.Null(item.LastModifiedById);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CannotMoveLockedItem()
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

        var result = await controller.MoveItem(FileToMove3Id, "StorageFileMoveTestParent/EmptyFolder");

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("lock on can't be moved", Assert.IsType<string>(response.Value));

        var item = await database.StorageItems.FindAsync(FileToMove3Id);
        Assert.NotNull(item);
        Assert.Null(item.MovedFromLocation);
        Assert.Null(item.LastModifiedById);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CannotMoveImportantItem()
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

        var result = await controller.MoveItem(FileToMove4Id, "StorageFileMoveTestParent/EmptyFolder");

        var response = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Important items can't", Assert.IsType<string>(response.Value));

        var item = await database.StorageItems.FindAsync(FileToMove4Id);
        Assert.NotNull(item);
        Assert.Null(item.MovedFromLocation);
        Assert.Null(item.LastModifiedById);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CannotMoveSpecialItem()
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

        var result = await controller.MoveItem(FileToMove5Id, "StorageFileMoveTestParent/EmptyFolder");

        // Special implies not writable so this doesn't hit the specific check we want so we test things like this
        Assert.IsNotType<OkObjectResult>(result);

        var item = await database.StorageItems.FindAsync(FileToMove5Id);
        Assert.NotNull(item);
        Assert.Null(item.MovedFromLocation);
        Assert.Null(item.LastModifiedById);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFilesController_CanDoSimpleMove()
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

        var result = await controller.MoveItem(FileToMove1Id, "StorageFileMoveTestParent/EmptyFolder");

        var response = Assert.IsType<OkObjectResult>(result);

        var resultPath = Assert.IsType<string>(response.Value);
        Assert.NotEmpty(resultPath);
        Assert.Equal("StorageFileMoveTestParent/EmptyFolder/Name1", resultPath);

        var item = await database.StorageItems.FindAsync(FileToMove1Id);
        Assert.NotNull(item);
        Assert.NotNull(item.MovedFromLocation);
        Assert.NotNull(item.LastModifiedById);

        // Verify the actual path matches what the API said to ensure there isn't an internal problem
        Assert.Equal(await item.ComputeStoragePath(database), resultPath);

        // We don't check the jobs mock for no other calls as a folder item recount should have been triggered
    }

    [Fact]
    public async Task StorageFilesController_AdminCanMoveToRoot()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = Substitute.For<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock,
            new EphemeralDataProtectionProvider(), jobClientMock);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(AdminUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result = await controller.MoveItem(FileToMove1Id, null);

        var response = Assert.IsType<OkObjectResult>(result);

        var resultPath = Assert.IsType<string>(response.Value);
        Assert.NotEmpty(resultPath);
        Assert.Equal("/Name1", resultPath);

        var item = await database.StorageItems.FindAsync(FileToMove1Id);
        Assert.NotNull(item);
        Assert.NotNull(item.MovedFromLocation);
        Assert.NotNull(item.LastModifiedById);

        // Skip the starting '/' that's not created by the storage path compute
        Assert.Equal(await item.ComputeStoragePath(database), resultPath.Substring(1));
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static NotificationsEnabledDb CreateReadOnlyDatabase()
    {
        var database = new NotificationsEnabledDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("StorageFileMoveTestsRO")
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
        // Setup folders and a few files to move around
        var parentFolder = new StorageItem
        {
            Id = TestParentFolderId,
            Name = "StorageFileMoveTestParent",
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

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FileToMove1Id,
            Name = "Name1",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = DeveloperUser.Id,
            Parent = parentFolder,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FileToMove2Id,
            Name = "Pretty long named file",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = NormalUser.Id,
            Parent = nonEmptyFolder,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FileToMove3Id,
            Name = "A nice name",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = DeveloperUser.Id,
            ModificationLocked = true,
            Parent = nonEmptyFolder,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FileToMove4Id,
            Name = "Another good name",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = DeveloperUser.Id,
            Important = true,
            Parent = nonEmptyFolder,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FileToMove5Id,
            Name = "A special thing",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = DeveloperUser.Id,
            Special = true,
            Parent = nonEmptyFolder,
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

        await database.SaveChangesAsync();

        await database.StorageItemDeleteInfos.AddAsync(new StorageItemDeleteInfo(deleted1.Id, FileAccess.User,
            FileAccess.User, "StorageFileRestoreTestParent/NonEmpty/Name1"));

        await database.SaveChangesAsync();
    }
}
