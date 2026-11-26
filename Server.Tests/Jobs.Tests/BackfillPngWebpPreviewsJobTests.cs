namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
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
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

public class BackfillPngWebpPreviewsJobTests
{
    [Fact]
    public async Task BackfillReplacesLegacyThumbnailAndAddsLosslessPagePreview()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ApplicationDbContext(options);
        await using var statusDatabase = new NotificationsEnabledDb(options,
            Substitute.For<IModelUpdateNotificationSender>());

        var png = new MediaFile("sample.png", Guid.NewGuid(), 1, null) { Processed = true };
        var webp = new MediaFile("sample.webp", Guid.NewGuid(), 1, null) { Processed = true };
        await database.MediaFiles.AddRangeAsync(png, webp);
        await database.SaveChangesAsync();
        Assert.Equal($"![sample.png](media:webp:{png.GlobalId}:f:n)",
            new MediaBrowserEntry(png.GetInfo()).GetMarkdown(false));
        Assert.Equal($"![sample.png](media:webp:{png.GlobalId})",
            new MediaBrowserEntry(png.GetDTO()).GetMarkdown());

        var operation = new ExecutedMaintenanceOperation("backfillPngWebpPreviews");
        await statusDatabase.ExecutedMaintenanceOperations.AddAsync(operation);
        await statusDatabase.SaveChangesAsync();

        var storage = Substitute.For<IMediaStorage>();
        storage.Configured.Returns(true);
        using var source = new Image<Rgba32>(9, 6);
        source[2, 3] = new Rgba32(12, 80, 190, 127);
        using var encoded = new MemoryStream();
        await source.SaveAsPngAsync(encoded);
        var originalBytes = encoded.ToArray();
        var originalPath = png.GetStoragePath(MediaFileSize.Original)!;
        storage.GetObjectContent(originalPath).Returns(_ => new MemoryStream(originalBytes, writable: false));

        var thumbnailPath = $"{png.GlobalId}/thumb.webp";
        var pagePath = $"{png.GlobalId}/page-fit.webp";
        var uploads = new Dictionary<string, byte[]> { [thumbnailPath] = [] };
        storage.DoesObjectExist(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => uploads.ContainsKey(call.Arg<string>()));
        storage.UploadFile(Arg.Any<string>(), Arg.Any<Stream>(), "image/webp", false, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var stream = call.Arg<Stream>();
                Assert.Equal(0, stream.Position);
                uploads[call.ArgAt<string>(0)] = ((MemoryStream)stream).ToArray();
                return Task.CompletedTask;
            });

        var job = new BackfillPngWebpPreviewsJob(NullLogger<BackfillPngWebpPreviewsJob>.Instance, database,
            statusDatabase, storage);
        await job.Execute(operation.Id, CancellationToken.None);

        Assert.False(operation.Failed);
        Assert.Equal(2, uploads.Count);
        foreach (var path in new[] { pagePath, thumbnailPath })
        {
            using var preview = Image.Load<Rgba32>(uploads[path]);
            Assert.IsType<WebpFormat>(preview.Metadata.DecodedImageFormat);
            Assert.Equal(WebpFileFormatType.Lossless, preview.Metadata.GetWebpMetadata().FileFormat);
            Assert.Equal(source[2, 3], preview[2, 3]);
        }

        await job.Execute(operation.Id, CancellationToken.None);
        await storage.Received(1).GetObjectContent(originalPath);
        await storage.DidNotReceive().GetObjectContent(webp.GetStoragePath(MediaFileSize.Original)!);

        uploads.Remove(thumbnailPath);
        await job.Execute(operation.Id, CancellationToken.None);
        await storage.Received(2).GetObjectContent(originalPath);
        Assert.True(uploads.ContainsKey(thumbnailPath));
    }
}
