namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Jobs.Maintenance;
using Server.Models;
using Server.Models.Pages;
using Server.Services;
using Shared.Models.Pages;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class BackfillMediaDimensionsJobTests
{
    [Fact]
    public async Task RunBackfillsMissingDimensionsAndContinuesAfterInvalidImage()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ApplicationDbContext(options);
        await using var statusDatabase = new NotificationsEnabledDb(options, Substitute.For<IModelUpdateNotificationSender>());

        var unreadable = new MediaFile("bad.png", Guid.NewGuid(), 1, null) { Id = 1, Processed = true };
        var pending = new MediaFile("image.png", Guid.NewGuid(), 1, null)
        {
            Id = 2,
            Processed = true,
            OriginalWidth = 5,
        };
        var complete = new MediaFile("complete.png", Guid.NewGuid(), 1, null)
        {
            Id = 3,
            Processed = true,
            OriginalWidth = 12,
            OriginalHeight = 8,
        };
        await database.MediaFiles.AddRangeAsync(unreadable, pending, complete);
        await database.SaveChangesAsync();

        var operation = new ExecutedMaintenanceOperation("backfillMediaDimensions");
        await statusDatabase.ExecutedMaintenanceOperations.AddAsync(operation);
        await statusDatabase.SaveChangesAsync();

        var storage = Substitute.For<IMediaStorage>();
        storage.Configured.Returns(true);
        storage.GetObjectContent(unreadable.GetStoragePath(MediaFileSize.Original)!)
            .Returns(_ => new MemoryStream([1, 2, 3]));
        var pendingPath = pending.GetStoragePath(MediaFileSize.Original)!;
        using var image = new Image<Rgba32>(9, 6);
        using var encoded = new MemoryStream();
        await image.SaveAsPngAsync(encoded);
        var imageBytes = encoded.ToArray();
        storage.GetObjectContent(pendingPath).Returns(_ => new MemoryStream(imageBytes, writable: false));

        var job = new BackfillMediaDimensionsJob(NullLogger<BackfillMediaDimensionsJob>.Instance, database,
            statusDatabase, storage);
        await job.Execute(operation.Id, CancellationToken.None);

        var updated = await database.MediaFiles.FindAsync(pending.Id);
        Assert.NotNull(updated);
        Assert.Equal(9, updated.OriginalWidth);
        Assert.Equal(6, updated.OriginalHeight);
        await storage.DidNotReceive().GetObjectContent(complete.GetStoragePath(MediaFileSize.Original)!);
        Assert.True(operation.Failed);
        Assert.NotNull(operation.FinishedAt);
        Assert.Equal("Updated dimensions for 1 media file(s). Failed: 1.", operation.ExtendedDescription);
    }
}
