namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests.Pages;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Fixtures;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using RevolutionaryWebApp.Server.Controllers.Pages;
using RevolutionaryWebApp.Server.Models;
using RevolutionaryWebApp.Server.Models.Pages;
using RevolutionaryWebApp.Server.Services;
using RevolutionaryWebApp.Shared.Models.Enums;
using RevolutionaryWebApp.Shared.Models.Pages;
using Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class MediaFileControllerTests : IDisposable
{
    private static byte[]? tinyPngBytes;

    private readonly XunitLogger<MediaFileController> logger;

    public MediaFileControllerTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<MediaFileController>(output);
    }

    /// <summary>
    ///   Generates a 1x1 transparent PNG byte array on the first call and then returns it (uses ImageSharp).
    /// </summary>
    /// <returns>Raw data for a PNG file</returns>
    public static byte[] TinyPngBytes()
    {
        if (tinyPngBytes != null)
            return tinyPngBytes;

        // 1x1 valid PNG (transparent)
        var pixels = new Argb32[] { new(255, 255, 255, 0) };
        var image = Image.LoadPixelData(pixels, 1, 1);

        using var memoryStream = new MemoryStream();
        image.SaveAsPng(memoryStream);

        return tinyPngBytes = memoryStream.ToArray();
    }

    [Fact]
    public async Task MediaFile_StartUploadReturnsUrlAndSchedulesCleanup()
    {
        // Arrange
        var db = CreateDatabase(nameof(MediaFile_StartUploadReturnsUrlAndSchedulesCleanup));

        var storage = Substitute.For<IUploadFileStorage>();
        storage.CreatePresignedUploadURL(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(ci =>
            {
                var path = ci.ArgAt<string>(0);
                return $"https://example/upload/{path}";
            });

        var jobs = Substitute.For<IBackgroundJobClient>();

        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Not used in these tests, but constructor requires it for view redirect endpoint
                ["MediaStorage:Download:URL"] = "https://cdn.example/",
            })
            .Build();

        var controller = new MediaFileController(logger, cfg, db, storage, new EphemeralDataProtectionProvider(), jobs);

        var user = CreateUser(42, GroupType.User, GroupType.Developer);
        await db.Users.AddAsync(user);
        var folder = new MediaFolder("TestFolder")
        {
            Id = 100,
            ContentWriteAccess = GroupType.User,
            ContentReadAccess = GroupType.User,
            OwnedById = user.Id,
            LastModifiedById = user.Id,
        };
        await db.MediaFolders.AddAsync(folder);
        await db.SaveChangesAsync();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = HttpContextMockHelpers.CreateContextWithUser(user),
        };

        var request = new UploadMediaFileRequestForm
        {
            MediaFileId = Guid.NewGuid(),
            Name = "image.png",
            Folder = folder.Id,
            Size = TinyPngBytes().Length,
            MetadataVisibility = GroupType.User,
            ModifyAccess = GroupType.User,
        };

        // Act
        var action = await controller.StartUpload(request);

        // Assert
        var ok = Assert.IsType<UploadRequestResponse>(Assert.IsType<ActionResult<UploadRequestResponse>>(action).Value);
        Assert.NotNull(ok.UploadUrl);
        Assert.False(string.IsNullOrWhiteSpace(ok.VerifyToken));

        // Ensure DB has created the file and updated quota
        var created = await db.MediaFiles.SingleAsync(m => m.GlobalId == request.MediaFileId);
        Assert.Equal(request.Name, created.Name);
        Assert.Equal(request.Size, created.OriginalFileSize);

        var updatedUser = await db.Users.FindAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.Equal(request.Size, updatedUser.UploadQuotaUsed);

        // Verify storage was called with the upload path for the created media file
        var expectedPath = created.GetUploadPath();
        storage.Received(1).CreatePresignedUploadURL(expectedPath, AppInfo.RemoteStorageUploadExpireTime);

        // Verify a cleanup job was scheduled
        Assert.NotEmpty(jobs.ReceivedCalls());
    }

    [Fact]
    public async Task MediaFile_FinishUploadVerifiesStorage()
    {
        // Arrange
        var db = CreateDatabase(nameof(MediaFile_FinishUploadVerifiesStorage));
        var storage = Substitute.For<IUploadFileStorage>();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MediaStorage:Download:URL"] = "https://cdn.example/",
            })
            .Build();

        var dataProtection = new EphemeralDataProtectionProvider();

        var controller = new MediaFileController(logger, cfg, db, storage, dataProtection, jobs);

        var user = CreateUser(55, GroupType.User, GroupType.Developer);
        await db.Users.AddAsync(user);
        var folder = new MediaFolder("FinishFolder")
        {
            Id = 200,
            ContentWriteAccess = GroupType.User,
            ContentReadAccess = GroupType.User,
            OwnedById = user.Id,
            LastModifiedById = user.Id,
        };
        await db.MediaFolders.AddAsync(folder);

        var file = new MediaFile("photo.png", Guid.NewGuid(), folder.Id, user.Id)
        {
            OriginalFileSize = TinyPngBytes().Length,
            MetadataVisibility = GroupType.User,
            ModifyAccess = GroupType.User,
        };
        await db.MediaFiles.AddAsync(file);
        await db.SaveChangesAsync();

        // Build a verification token as controller would return earlier
        var protector = dataProtection.CreateProtector("MediaFileController.Upload.v1").ToTimeLimitedDataProtector();
        var tokenJson =
            System.Text.Json.JsonSerializer.Serialize(
                new MediaFileController.UploadVerifyToken { TargetItem = file.Id });
        var token = protector.Protect(tokenJson, AppInfo.RemoteStorageUploadExpireTime);

        // Mock storage interactions
        var uploadPath = file.GetUploadPath();
        var processingPath = file.GetIntermediateProcessingPath();

        storage.GetObjectSize(uploadPath).Returns(file.OriginalFileSize);
        storage.MoveObject(uploadPath, processingPath).Returns(Task.CompletedTask);
        storage.GetObjectSize(processingPath).Returns(file.OriginalFileSize);

        // Provide a tiny valid PNG so Image.IdentifyAsync succeeds
        var pngBytes = TinyPngBytes();
        storage.GetObjectContent(processingPath)
            .Returns(_ => new MemoryStream(pngBytes, writable: false));

        // Act
        var result = await controller.ReportFinishedUpload(new TokenForm { Token = token });

        // This depends on ImageSharp working, which should work in all test environments
        Assert.IsAssignableFrom<OkResult>(result);

        // Validate calls to storage
        await storage.Received(1).GetObjectSize(uploadPath);
        await storage.Received(1).MoveObject(uploadPath, processingPath);
        await storage.Received(1).GetObjectSize(processingPath);
        await storage.Received(1).GetObjectContent(processingPath);

        // Verify something was scheduled
        Assert.NotEmpty(jobs.ReceivedCalls());
    }

    [Fact]
    public async Task MediaFile_FinishUploadErrorsOnSizeMismatch()
    {
        // Arrange
        var db = CreateDatabase(nameof(MediaFile_FinishUploadErrorsOnSizeMismatch));
        var storage = Substitute.For<IUploadFileStorage>();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MediaStorage:Download:URL"] = "https://cdn.example/",
            })
            .Build();

        var dataProtection = new EphemeralDataProtectionProvider();

        var controller = new MediaFileController(logger, cfg, db, storage, dataProtection, jobs);

        var user = CreateUser(77, GroupType.User, GroupType.Developer);
        await db.Users.AddAsync(user);
        var folder = new MediaFolder("MismatchFolder")
        {
            Id = 300,
            ContentWriteAccess = GroupType.User,
            ContentReadAccess = GroupType.User,
            OwnedById = user.Id,
            LastModifiedById = user.Id,
        };
        await db.MediaFolders.AddAsync(folder);

        var file = new MediaFile("mismatch.png", Guid.NewGuid(), folder.Id, user.Id)
        {
            OriginalFileSize = TinyPngBytes().Length,
            MetadataVisibility = GroupType.User,
            ModifyAccess = GroupType.User,
        };
        await db.MediaFiles.AddAsync(file);
        await db.SaveChangesAsync();

        // Build a verification token as controller would return earlier
        var protector = dataProtection.CreateProtector("MediaFileController.Upload.v1").ToTimeLimitedDataProtector();
        var tokenJson =
            System.Text.Json.JsonSerializer.Serialize(
                new MediaFileController.UploadVerifyToken { TargetItem = file.Id });
        var token = protector.Protect(tokenJson, AppInfo.RemoteStorageUploadExpireTime);

        // Mock storage interactions: first size check mismatches
        var uploadPath = file.GetUploadPath();
        storage.GetObjectSize(uploadPath).Returns(file.OriginalFileSize - 1);

        // Deleting partial upload should be attempted
        storage.DeleteObject(uploadPath).Returns(Task.CompletedTask);

        // Act
        var result = await controller.ReportFinishedUpload(new TokenForm { Token = token });

        // Assert: should be BadRequest and not proceed to move
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var message = Assert.IsType<string>(bad.Value);
        Assert.Contains("partially successful", message, StringComparison.OrdinalIgnoreCase);

        await storage.Received(1).GetObjectSize(uploadPath);
        await storage.Received(1).DeleteObject(uploadPath);

        await storage.DidNotReceiveWithAnyArgs().MoveObject(string.Empty, string.Empty);
        await storage.DidNotReceiveWithAnyArgs().GetObjectContent(string.Empty);

        // No processing job should be scheduled
        Assert.Empty(jobs.ReceivedCalls());
    }

    [Fact]
    public async Task MediaFile_UploadEndToEndSucceeds()
    {
        // Arrange common services
        var db = CreateDatabase(nameof(MediaFile_UploadEndToEndSucceeds));
        var storage = Substitute.For<IUploadFileStorage>();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MediaStorage:Download:URL"] = "https://cdn.example/",
            })
            .Build();

        var dataProtection = new EphemeralDataProtectionProvider();

        var controller = new MediaFileController(logger, cfg, db, storage, dataProtection, jobs);

        var user = CreateUser(89, GroupType.User, GroupType.Developer);
        await db.Users.AddAsync(user);
        var folder = new MediaFolder("E2EFolder")
        {
            Id = 400,
            ContentWriteAccess = GroupType.Developer,
            ContentReadAccess = GroupType.User,
            OwnedById = user.Id,
            LastModifiedById = user.Id,
        };
        await db.MediaFolders.AddAsync(folder);
        await db.SaveChangesAsync();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = HttpContextMockHelpers.CreateContextWithUser(user),
        };

        var size = TinyPngBytes().Length;
        var request = new UploadMediaFileRequestForm
        {
            MediaFileId = Guid.NewGuid(),
            Name = "end-to-end.png",
            Folder = folder.Id,
            Size = size,
            MetadataVisibility = GroupType.User,
            ModifyAccess = GroupType.Developer,
        };

        storage.CreatePresignedUploadURL(Arg.Any<string>(), Arg.Any<TimeSpan>())
            .Returns(ci =>
            {
                var path = ci.ArgAt<string>(0);
                return $"https://example/upload/{path}";
            });

        // Act 1: start upload
        var start = await controller.StartUpload(request);
        var startPayload =
            Assert.IsType<UploadRequestResponse>(Assert.IsType<ActionResult<UploadRequestResponse>>(start).Value);
        Assert.False(string.IsNullOrWhiteSpace(startPayload.VerifyToken));

        // Determine paths for finish stage
        var created = await db.MediaFiles.SingleAsync(m => m.GlobalId == request.MediaFileId);
        var uploadPath = created.GetUploadPath();
        var processingPath = created.GetIntermediateProcessingPath();

        // Set up storage to pass verify stage
        storage.GetObjectSize(uploadPath).Returns(size);
        storage.MoveObject(uploadPath, processingPath).Returns(Task.CompletedTask);
        storage.GetObjectSize(processingPath).Returns(size);
        storage.GetObjectContent(processingPath).Returns(_ => new MemoryStream(TinyPngBytes(), writable: false));

        // Act 2: finish upload
        var finish = await controller.ReportFinishedUpload(new TokenForm { Token = startPayload.VerifyToken });

        // Assert: success and calls made
        Assert.IsType<OkResult>(finish);
        await storage.Received(1).GetObjectSize(uploadPath);
        await storage.Received(1).MoveObject(uploadPath, processingPath);
        await storage.Received(1).GetObjectSize(processingPath);
        await storage.Received(1).GetObjectContent(processingPath);

        // Ensure a processing job was scheduled
        Assert.NotEmpty(jobs.ReceivedCalls());

        // Get the file from the DB to check
        var file = await db.MediaFiles.SingleAsync(m => m.GlobalId == request.MediaFileId);
        Assert.Equal(request.Name, file.Name);
        Assert.Equal(request.Size, file.OriginalFileSize);
        Assert.Equal(request.MetadataVisibility, file.MetadataVisibility);
        Assert.Equal(request.ModifyAccess, file.ModifyAccess);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static NotificationsEnabledDb CreateDatabase(string name)
    {
        var notifications = Substitute.For<IModelUpdateNotificationSender>();
        var database = new EditableInMemoryDatabaseFixtureWithNotifications(notifications, name);

        return database.NotificationsEnabledDatabase;
    }

    private static User CreateUser(long id, params GroupType[] groups)
    {
        var user = new User($"user{id}@example.com", $"User{id}")
        {
            Id = id,
            Local = true,
            Groups = new List<UserGroup>(),
        };

        // This bypasses the database, but that is acceptable for in-memory testing purposes
        user.ForceResolveGroupsForTesting(new RevolutionaryWebApp.Shared.Models.CachedUserGroups(groups));

        return user;
    }
}
