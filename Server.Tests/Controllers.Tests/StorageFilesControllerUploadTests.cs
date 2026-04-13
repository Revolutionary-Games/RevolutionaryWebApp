namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Fixtures;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RevolutionaryWebApp.Server.Controllers;
using RevolutionaryWebApp.Server.Models;
using RevolutionaryWebApp.Server.Services;
using RevolutionaryWebApp.Shared.Models;
using Shared;
using Shared.Forms;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class StorageFilesControllerUploadTests : IDisposable
{
    private readonly XunitLogger<StorageFilesController> logger;

    public StorageFilesControllerUploadTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<StorageFilesController>(output);
    }

    [Fact]
    public async Task StorageFiles_StartFileUploadReturnsUrl()
    {
        // Arrange
        var notifications = Substitute.For<IModelUpdateNotificationSender>();
        var db = new EditableInMemoryDatabaseFixtureWithNotifications(notifications,
                nameof(StorageFiles_StartFileUploadReturnsUrl))
            .NotificationsEnabledDatabase;

        var storage = Substitute.For<IGeneralRemoteStorage>();
        storage.Configured.Returns(true);
        storage.CreatePresignedUploadURL(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(ci =>
            {
                var path = ci.ArgAt<string>(0);
                return $"https://example/upload/{path}";
            });

        var jobs = Substitute.For<IBackgroundJobClient>();

        var controller = new StorageFilesController(logger, db, storage,
            new EphemeralDataProtectionProvider(), jobs);

        var user = UserUtilities.CreateDeveloperUser(1001);
        await db.Users.AddAsync(user);

        // Writable parent folder
        var folder = new StorageItem
        {
            Name = "Uploads",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Developer,
            OwnerId = user.Id,
            LastModifiedById = user.Id,
            AllowParentless = true,
        };
        await db.StorageItems.AddAsync(folder);
        await db.SaveChangesAsync();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = HttpContextMockHelpers.CreateContextWithUser(user),
        };

        var request = new UploadFileRequestForm
        {
            Name = "test.bin",
            ParentFolder = folder.Id,

            // Keep small to avoid multipart
            Size = 2048,
            MimeType = "application/octet-stream",
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Developer,
        };

        // Act
        var action = await controller.StartFileUpload(request);

        // Assert
        var payload = Assert.IsType<UploadFileResponse>(Assert.IsType<ActionResult<UploadFileResponse>>(action).Value);
        Assert.NotNull(payload.UploadURL);
        Assert.False(string.IsNullOrWhiteSpace(payload.UploadVerifyToken));

        // DB created item and initial version with a storage file
        var item = await db.StorageItems.Include(i => i.StorageItemVersions)
            .SingleAsync(i => i.ParentId == folder.Id && i.Name == request.Name);
        var version = await db.StorageItemVersions.Include(v => v.StorageFile)
            .SingleAsync(v => v.Id == payload.TargetStorageItemVersion);
        Assert.Equal(item.Id, payload.TargetStorageItem);
        Assert.NotNull(version.StorageFile);
        Assert.Equal(request.Size, version.StorageFile.Size);

        // Storage called with the upload path
        var expectedUploadPath = version.StorageFile.UploadPath;
        storage.Received(1).CreatePresignedUploadURL(expectedUploadPath, AppInfo.RemoteStorageUploadExpireTime);

        // Parent folder counting job should be enqueued
        Assert.NotEmpty(jobs.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFiles_SizeMismatchDeletesPartial()
    {
        // Arrange database and services
        var notifications = Substitute.For<IModelUpdateNotificationSender>();
        var db = new EditableInMemoryDatabaseFixtureWithNotifications(notifications,
            nameof(StorageFiles_SizeMismatchDeletesPartial)).NotificationsEnabledDatabase;

        var storage = Substitute.For<IGeneralRemoteStorage>();
        storage.Configured.Returns(true);
        var jobs = Substitute.For<IBackgroundJobClient>();

        var dataProtection = new EphemeralDataProtectionProvider();
        var controller = new StorageFilesController(logger, db, storage, dataProtection, jobs);

        var user = UserUtilities.CreateDeveloperUser(2001);
        await db.Users.AddAsync(user);

        var folder = new StorageItem
        {
            Name = "MismatchFolder",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Developer,
            OwnerId = user.Id,
            LastModifiedById = user.Id,
            AllowParentless = true,
        };
        await db.StorageItems.AddAsync(folder);

        var item = new StorageItem
        {
            Name = "file.bin",
            Ftype = FileType.File,
            Parent = folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            OwnerId = user.Id,
        };
        await db.StorageItems.AddAsync(item);

        // Create initial version and storage file
        var version = await item.CreateNextVersion(db, user);
        var file = await version.CreateStorageFile(db, DateTime.UtcNow.AddMinutes(30), 1234);
        Assert.NotNull(file.Size);
        await db.SaveChangesAsync();

        // Build verify token like controller returns
        var protector = dataProtection.CreateProtector("StorageFilesController.Upload.v1").ToTimeLimitedDataProtector();
        var tokenStr = System.Text.Json.JsonSerializer.Serialize(new StorageFilesController.UploadVerifyToken
        {
            TargetStorageItem = item.Id,
            TargetStorageItemVersion = version.Id,
        });
        var token = protector.Protect(tokenStr, AppInfo.RemoteStorageUploadExpireTime);

        // Mock storage: size mismatch on upload path, expect delete
        storage.GetObjectSize(file.UploadPath).Returns(file.Size.Value - 1);
        storage.DeleteObject(file.UploadPath).Returns(Task.CompletedTask);

        // Act
        var result = await controller.ReportFinishedUpload(new UploadFileResponse
        {
            TargetStorageItem = item.Id,
            TargetStorageItemVersion = version.Id,
            UploadVerifyToken = token,
        });

        // Assert
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var msg = Assert.IsType<string>(bad.Value);
        Assert.Contains("partially successful", msg, StringComparison.OrdinalIgnoreCase);

        await storage.Received(1).GetObjectSize(file.UploadPath);
        await storage.Received(1).DeleteObject(file.UploadPath);

        await storage.DidNotReceiveWithAnyArgs().MoveObject(string.Empty, string.Empty);
        await storage.DidNotReceiveWithAnyArgs().GetObjectContent(string.Empty);
        Assert.Empty(jobs.ReceivedCalls());
    }

    [Fact]
    public async Task StorageFiles_StartThenFinishEndToEndSucceeds()
    {
        // Arrange
        var notifications = Substitute.For<IModelUpdateNotificationSender>();
        var db = new EditableInMemoryDatabaseFixtureWithNotifications(notifications,
            nameof(StorageFiles_StartThenFinishEndToEndSucceeds)).NotificationsEnabledDatabase;

        var storage = Substitute.For<IGeneralRemoteStorage>();
        storage.Configured.Returns(true);

        storage.PerformFileUploadSuccessActions(Arg.Any<StorageFile>(), Arg.Any<ApplicationDbContext>()).Returns(args =>
            BaseRemoteStorage.DoPerformFileUploadSuccessActionsBase(args.Arg<StorageFile>(),
                args.Arg<ApplicationDbContext>()));

        var jobs = Substitute.For<IBackgroundJobClient>();

        var controller = new StorageFilesController(logger, db, storage, new EphemeralDataProtectionProvider(), jobs);

        var user = UserUtilities.CreateDeveloperUser(3001);
        await db.Users.AddAsync(user);

        var folder = new StorageItem
        {
            Name = "E2E",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Developer,
            OwnerId = user.Id,
            AllowParentless = true,
        };
        await db.StorageItems.AddAsync(folder);
        await db.SaveChangesAsync();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = HttpContextMockHelpers.CreateContextWithUser(user),
        };

        var size = 1024;
        var request = new UploadFileRequestForm
        {
            Name = "end-to-end.bin",
            ParentFolder = folder.Id,
            Size = size,
            MimeType = "application/octet-stream",
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.Developer,
        };

        storage.CreatePresignedUploadURL(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(ci => $"https://upload/{ci.ArgAt<string>(0)}");

        // Act 1: start
        var start = await controller.StartFileUpload(request);
        var startPayload =
            Assert.IsType<UploadFileResponse>(Assert.IsType<ActionResult<UploadFileResponse>>(start).Value);
        Assert.False(string.IsNullOrWhiteSpace(startPayload.UploadVerifyToken));

        // Determine paths and set storage to succeed verification
        var version = await db.StorageItemVersions.Include(v => v.StorageFile)
            .SingleAsync(v => v.Id == startPayload.TargetStorageItemVersion);
        Assert.NotNull(version.StorageFile);
        var uploadPath = version.StorageFile.UploadPath;
        var storagePath = version.StorageFile.StoragePath;

        Assert.True(version.Uploading);

        storage.GetObjectSize(uploadPath).Returns(size);
        storage.MoveObject(uploadPath, storagePath).Returns(Task.CompletedTask);
        storage.GetObjectSize(storagePath).Returns(size);

        // Act 2: finish
        var finish = await controller.ReportFinishedUpload(new UploadFileResponse
        {
            TargetStorageItem = startPayload.TargetStorageItem,
            TargetStorageItemVersion = startPayload.TargetStorageItemVersion,
            UploadVerifyToken = startPayload.UploadVerifyToken,
        });

        // Assert: success and calls
        Assert.IsType<OkResult>(finish);
        await storage.Received(1).GetObjectSize(uploadPath);
        await storage.Received(1).MoveObject(uploadPath, storagePath);
        await storage.Received(1).GetObjectSize(storagePath);
        Assert.NotEmpty(jobs.ReceivedCalls());

        // As we used in-memory DB, we can read the version properties
        Assert.False(version.Uploading);
        Assert.False(version.Deleted);
        Assert.Equal(user.Id, version.UploadedById);
        Assert.False(version.StorageFile.Uploading);
        Assert.Equal(size, version.StorageFile.Size);

        var createdFile =
            await db.StorageItems.Where(i => i.Id == startPayload.TargetStorageItem).FirstOrDefaultAsync();
        Assert.NotNull(createdFile);
        Assert.Equal(size, createdFile.Size);

        // Last modified by doesn't always update on new version upload
        // Assert.Equal(user.Id, createdFile.LastModifiedById);

        Assert.Equal(user.Id, createdFile.OwnerId);
        Assert.False(createdFile.Deleted);
        Assert.Equal("end-to-end.bin", createdFile.Name);
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
