namespace ThriveDevCenter.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Filters;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class StorageFileDeleteTests : IDisposable
{
    private const long TestParentFolderId = 675000;
    private const long EmptyFolderId = 675001;
    private const long NonEmptyFolderId = 675002;
    private const long SubFolderId = 675003;

    private const long FolderInRootId = 675004;
    private const long DeveloperOnlyFolderId = 675005;

    private const long SelfLockedFolderId = 675006;
    private const long SpecialFolderId = 675007;

    private const long FileToDeleteId = 675008;
    private const long FileToDeleteId2 = 675009;

    private static readonly Mock<IModelUpdateNotificationSender> ReadonlyDbNotifications = new();

    private static readonly Lazy<NotificationsEnabledDb> NonWritingTestDb = new(CreateReadOnlyDatabase);

    private static readonly User DeveloperUser = UserUtilities.CreateDeveloperUser(100);
    private static readonly User NormalUser = UserUtilities.CreateNormalUser(101);
    private static readonly User AdminUser = UserUtilities.CreateAdminUser(102);

    private readonly XunitLogger<StorageFilesController> logger;

    public StorageFileDeleteTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<StorageFilesController>(output);
    }

    [Fact]
    public async Task StorageFilesController_NonEmptyFolderDeleteFails()
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

        var result = await controller.DeleteOrTrash(NonEmptyFolderId);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);

        var responseString = Assert.IsType<string>(response.Value);

        Assert.Contains("empty folders can be deleted", responseString);

        var item = await database.StorageItems.FindAsync(NonEmptyFolderId);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_NonLoggedInCannotDelete()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(null);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.DeleteOrTrash(EmptyFolderId);

        Assert.IsType<NotFoundResult>(result);

        var item = await database.StorageItems.FindAsync(EmptyFolderId);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_CannotDeleteWithoutRightPermissions()
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

        var result = await controller.DeleteOrTrash(DeveloperOnlyFolderId);

        Assert.IsType<NotFoundResult>(result);

        var item = await database.StorageItems.FindAsync(DeveloperOnlyFolderId);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_RootContentNotDeletableByNonAdmin()
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

        var exception = await Assert.ThrowsAsync<HttpResponseException>(() => controller.DeleteOrTrash(FolderInRootId));

        Assert.Contains("Only admins", Assert.IsType<string>(exception.Value));

        var item = await database.StorageItems.FindAsync(FolderInRootId);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_CannotDeleteSelfLockedFolder()
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

        var result = await controller.DeleteOrTrash(SelfLockedFolderId);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);

        var responseString = Assert.IsType<string>(response.Value);

        Assert.Contains("properties lock on", responseString);

        var item = await database.StorageItems.FindAsync(SelfLockedFolderId);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_CannotDeleteSpecialFolder()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        var database = NonWritingTestDb.Value;
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(AdminUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.DeleteOrTrash(SpecialFolderId);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);

        var responseString = Assert.IsType<string>(response.Value);

        Assert.Contains("Special item", responseString);

        var item = await database.StorageItems.FindAsync(SpecialFolderId);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_CannotDeleteImportantFile()
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

        var result = await controller.DeleteOrTrash(FileToDeleteId2);

        var response = Assert.IsAssignableFrom<BadRequestObjectResult>(result);

        var responseString = Assert.IsType<string>(response.Value);

        Assert.Contains("Important items can't be deleted", responseString);

        var item = await database.StorageItems.FindAsync(FileToDeleteId2);
        Assert.NotNull(item);
        Assert.False(item.Deleted);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_CanDeleteEmptyFolder()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database = await GetWritableDatabase(nameof(StorageFilesController_CanDeleteEmptyFolder));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.DeleteOrTrash(EmptyFolderId);

        Assert.IsAssignableFrom<OkResult>(result);

        var item = await database.StorageItems.FindAsync(EmptyFolderId);
        Assert.Null(item);
    }

    [Fact]
    public async Task StorageFilesController_CanDeleteFullFolderAfterClear()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_CanDeleteFullFolderAfterClear));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        await controller.DeleteOrTrash(SubFolderId);

        var item = await database.StorageItems.FindAsync(SubFolderId);
        Assert.Null(item);

        item = await database.StorageItems.FindAsync(NonEmptyFolderId);
        Assert.NotNull(item);

        var result = await controller.DeleteOrTrash(NonEmptyFolderId);

        Assert.IsAssignableFrom<OkResult>(result);

        item = await database.StorageItems.FindAsync(NonEmptyFolderId);
        Assert.Null(item);
    }

    [Fact]
    public async Task StorageFilesController_AdminCanDeleteRootItem()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database = await GetWritableDatabase(nameof(StorageFilesController_AdminCanDeleteRootItem));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(AdminUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.DeleteOrTrash(FolderInRootId);

        Assert.IsAssignableFrom<OkResult>(result);

        var item = await database.StorageItems.FindAsync(FolderInRootId);
        Assert.Null(item);

        jobClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StorageFilesController_DeletingFileWorks()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database = await GetWritableDatabase(nameof(StorageFilesController_DeletingFileWorks));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var trashContents = await controller.GetFolderContents(nameof(StorageItem.Id), SortDirection.Ascending, 0, 10,
            TestFileUtilities.TestTrashFolderId);

        Assert.NotNull(trashContents.Value);

        Assert.Empty(trashContents.Value.Results);

        var result = await controller.DeleteOrTrash(FileToDeleteId);

        Assert.IsAssignableFrom<OkResult>(result);

        var item = await database.StorageItems.FindAsync(FileToDeleteId);
        Assert.NotNull(item);
        Assert.True(item.Deleted);
        Assert.Equal(FileAccess.Nobody, item.WriteAccess);
        Assert.Equal(FileAccess.OwnerOrAdmin, item.ReadAccess);

        trashContents = await controller.GetFolderContents(nameof(StorageItem.Id), SortDirection.Ascending, 0, 10,
            TestFileUtilities.TestTrashFolderId);

        Assert.NotNull(trashContents.Value);

        Assert.NotEmpty(trashContents.Value.Results);

        Assert.Equal(FileToDeleteId, trashContents.Value.Results.First().Id);

        var deletedInfo = await database.StorageItemDeleteInfos.FindAsync(FileToDeleteId);

        Assert.NotNull(deletedInfo);
        Assert.Equal(TestParentFolderId, deletedInfo.OriginalFolderId);
        Assert.Equal("StorageFileDeleteTestParent/A file", deletedInfo.OriginalFolderPath);
    }

    [Fact]
    public async Task StorageFilesController_AdminSeesEveryoneDeletedFiles()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_AdminSeesEveryoneDeletedFiles));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        Assert.NotEqual(AdminUser.Id, DeveloperUser.Id);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.DeleteOrTrash(FileToDeleteId);

        Assert.IsAssignableFrom<OkResult>(result);

        var item = await database.StorageItems.FindAsync(FileToDeleteId);
        Assert.NotNull(item);
        Assert.True(item.Deleted);
        Assert.Equal(DeveloperUser.Id, item.OwnerId);

        httpContextMock = HttpContextMockHelpers.CreateContextWithUser(AdminUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var trashContents = await controller.GetFolderContents(nameof(StorageItem.Id), SortDirection.Ascending, 0, 10,
            TestFileUtilities.TestTrashFolderId);

        Assert.NotNull(trashContents.Value);
        Assert.NotEmpty(trashContents.Value.Results);
        Assert.Equal(FileToDeleteId, trashContents.Value.Results.First().Id);

        // The job to recount the folder items gets queued so we can't check that the code hasn't also accidentally
        // enqueued a job to try to delete storage files or something like that
    }

    [Fact]
    public async Task StorageFilesController_UserDoesNotSeeOtherPeopleDeletedFiles()
    {
        var jobClientMock = new Mock<IBackgroundJobClient>();

        await using var database =
            await GetWritableDatabase(nameof(StorageFilesController_UserDoesNotSeeOtherPeopleDeletedFiles));
        var remoteStorageMock = new Mock<IGeneralRemoteStorage>();

        var controller = new StorageFilesController(logger, database, remoteStorageMock.Object,
            new EphemeralDataProtectionProvider(), jobClientMock.Object);

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(NormalUser);

        Assert.NotEqual(DeveloperUser.Id, NormalUser.Id);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var result = await controller.DeleteOrTrash(FileToDeleteId);

        Assert.IsAssignableFrom<OkResult>(result);

        var item = await database.StorageItems.FindAsync(FileToDeleteId);
        Assert.NotNull(item);
        Assert.True(item.Deleted);

        httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
        };

        var trashContents = await controller.GetFolderContents(nameof(StorageItem.Id), SortDirection.Ascending, 0, 10,
            TestFileUtilities.TestTrashFolderId);

        Assert.NotNull(trashContents.Value);
        Assert.Empty(trashContents.Value.Results);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static NotificationsEnabledDb CreateReadOnlyDatabase()
    {
        var database = new NotificationsEnabledDb(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("StorageFileDeleteTestsRO").Options,
            ReadonlyDbNotifications.Object);
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
        var parentFolder = new StorageItem
        {
            Id = TestParentFolderId,
            Name = "StorageFileDeleteTestParent",
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
            Name = "Sub",
            Ftype = FileType.Folder,
            Parent = nonEmptyFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FolderInRootId,
            Name = "FolderInRootToDelete",
            Ftype = FileType.Folder,
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
            Id = SelfLockedFolderId,
            Name = "SelfLocked",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            ModificationLocked = true,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = SpecialFolderId,
            Name = "Special",
            Ftype = FileType.Folder,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            Special = true,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FileToDeleteId,
            Name = "A file",
            Ftype = FileType.File,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = FileToDeleteId2,
            Name = "A file 2",
            Ftype = FileType.File,
            Parent = parentFolder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            Important = true,
        });

        await TestFileUtilities.CreateTrashFolder(database);

        await database.SaveChangesAsync();
    }
}
