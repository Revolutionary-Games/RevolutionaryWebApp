namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RevolutionaryWebApp.Server.Jobs.Pages;
using RevolutionaryWebApp.Server.Models;
using RevolutionaryWebApp.Server.Models.Pages;
using RevolutionaryWebApp.Server.Services;
using RevolutionaryWebApp.Shared.Models.Enums;
using RevolutionaryWebApp.Shared.Models.Pages;
using RevolutionaryWebApp.Shared.Utilities;
using Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class ProcessUploadedImageJobTests : IDisposable
{
    private readonly XunitLogger<ProcessUploadedImageJob> logger;

    public ProcessUploadedImageJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<ProcessUploadedImageJob>(output);
    }

    [Fact]
    public async Task ProcessUploadedImageJob_ProcessesGifAndDownsizesEfficiently()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(ProcessUploadedImageJob_ProcessesGifAndDownsizesEfficiently)).Options;
        var database = new NotificationsEnabledDb(dbOptions, notificationsMock);

        var user = new User("test@example.com", "TestUser");
        await database.Users.AddAsync(user);

        var folder = new MediaFolder("TestFolder")
        {
            Id = 1,
            OwnedById = user.Id,
            LastModifiedById = user.Id,
        };
        await database.MediaFolders.AddAsync(folder);

        var globalId = Guid.NewGuid();
        var mediaFile = new MediaFile("animation.gif", globalId, folder.Id, user.Id)
        {
            Id = 10,
            MetadataVisibility = GroupType.User,
            ModifyAccess = GroupType.User,
            Processed = false,
        };
        await database.MediaFiles.AddAsync(mediaFile);
        await database.SaveChangesAsync();

        Assert.True(1000 > AppInfo.MediaResolutionPage);

        // Create an animated 1000x800 GIF (larger than Page 900 resolution)
        using var testImage = new Image<Rgba32>(1000, 800);
        for (int i = 0; i < 4; ++i)
        {
            using var frame = new Image<Rgba32>(1000, 800);
            for (int y = 0; y < 800; ++y)
            {
                for (int x = 0; x < 1000; ++x)
                {
                    frame[x, y] = (x / 100 + y / 100 + i) % 2 == 0 ?
                        Color.Navy.ToPixel<Rgba32>() :
                        Color.White.ToPixel<Rgba32>();
                }
            }

            if (i == 0)
            {
                for (int y = 0; y < 800; ++y)
                {
                    for (int x = 0; x < 1000; ++x)
                    {
                        testImage[x, y] = frame[x, y];
                    }
                }
            }
            else
            {
                testImage.Frames.AddFrame(frame.Frames.RootFrame);
            }
        }

        using var rawGifStream = new MemoryStream();
        await testImage.SaveAsGifAsync(rawGifStream);
        var rawGifBytes = rawGifStream.ToArray();

        var processPath = mediaFile.GetIntermediateProcessingPath();
        var uploadedFiles = new Dictionary<string, byte[]>();

        var uploadFileStorage = Substitute.For<IUploadFileStorage>();
        uploadFileStorage.GetObjectContent(processPath)
            .Returns(_ => new MemoryStream(rawGifBytes));

        var mediaStorage = Substitute.For<IMediaStorage>();
        mediaStorage.UploadFile(Arg.Any<string>(), Arg.Any<Stream>(), Arg.Any<string>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                var path = ci.ArgAt<string>(0);
                var stream = ci.ArgAt<Stream>(1);
                var mem = new MemoryStream();
                await stream.CopyToAsync(mem);
                uploadedFiles[path] = mem.ToArray();
            });

        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var job = new ProcessUploadedImageJob(logger, database, uploadFileStorage, mediaStorage, jobClientMock);
        await job.Execute(mediaFile.Id, processPath, CancellationToken.None);

        Assert.True(mediaFile.Processed);

        var originalPath = MediaFileExtensions.GetStoragePath(mediaFile, MediaFileSize.Original);
        var pagePath = MediaFileExtensions.GetStoragePath(mediaFile, MediaFileSize.FitPage);
        var thumbPath = MediaFileExtensions.GetStoragePath(mediaFile, MediaFileSize.Thumbnail);

        Assert.True(uploadedFiles.ContainsKey(originalPath));
        Assert.True(uploadedFiles.ContainsKey(pagePath));
        Assert.True(uploadedFiles.ContainsKey(thumbPath));

        // Downscaled page image should not blow up in size compared to the original
        var pageBytes = uploadedFiles[pagePath];
        var originalSavedBytes = uploadedFiles[originalPath];

        Assert.True(pageBytes.Length <= originalSavedBytes.Length,
            $"Downsized page image size ({pageBytes.Length} bytes) should be less than or equal to original " +
            $"image size ({originalSavedBytes.Length} bytes)");
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
