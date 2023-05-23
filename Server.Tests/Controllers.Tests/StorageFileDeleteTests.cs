namespace ThriveDevCenter.Server.Tests.Controllers.Tests;

using System;
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

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(DeveloperUser);

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

        await database.SaveChangesAsync();
    }
}
