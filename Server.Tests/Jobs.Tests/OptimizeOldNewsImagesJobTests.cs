namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Jobs.Maintenance;
using Server.Models;
using Server.Models.Pages;
using Server.Services;
using Shared.Models.Pages;
using SharedBase.Utilities;
using Xunit;

public class OptimizeOldNewsImagesJobTests
{
    [Fact]
    public async Task UpdatesOnlyProcessedPngImagesInPublishedOlderPostsAndPreservesHistory()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ApplicationDbContext(options);
        await using var statusDatabase = new NotificationsEnabledDb(options,
            Substitute.For<IModelUpdateNotificationSender>());

        var png = new MediaFile("image.png", Guid.NewGuid(), 1, null) { Processed = true };
        var pending = new MediaFile("pending.png", Guid.NewGuid(), 1, null);
        var webp = new MediaFile("image.webp", Guid.NewGuid(), 1, null) { Processed = true };
        await database.MediaFiles.AddRangeAsync(png, pending, webp);

        var unchanged = $"![Pending](media:png:{pending.GlobalId})\n\n" +
            $"![Wrong type](media:png:{webp.GlobalId})\n\n" +
            $"![External](https://example.com/{png.GlobalId}/page-fit.png)\n\n" +
            $"`![Code](media:png:{png.GlobalId})`\n\n" +
            $"[Ordinary link](media:png:{png.GlobalId})";
        var original = $"![Image](media:png:{png.GlobalId}:f:n 'Caption')\n\n" +
            $"![Second](media:.png:{png.GlobalId})\n\n" + unchanged;
        var expected = $"![Image](media:webp:{png.GlobalId}:f:n 'Caption')\n\n" +
            $"![Second](media:.webp:{png.GlobalId})\n\n" + unchanged;
        var old = new VersionedPage("Old news")
        {
            Type = PageType.Post,
            Visibility = PageVisibility.Public,
            PublishedAt = DateTime.UtcNow.AddMonths(-7),
            LatestContent = original,
            LastEditComment = "Original comment",
        };
        var recent = new VersionedPage("Recent news")
        {
            Type = PageType.Post,
            Visibility = PageVisibility.Public,
            PublishedAt = DateTime.UtcNow.AddMonths(-5),
            LatestContent = original,
        };
        var draft = new VersionedPage("Draft news")
        {
            Type = PageType.Post,
            Visibility = PageVisibility.HiddenDraft,
            PublishedAt = old.PublishedAt,
            LatestContent = original,
        };
        await database.VersionedPages.AddRangeAsync(old, recent, draft);
        await database.SaveChangesAsync();

        var operation = new ExecutedMaintenanceOperation("optimizeOldNewsImages");
        await statusDatabase.ExecutedMaintenanceOperations.AddAsync(operation);
        await statusDatabase.SaveChangesAsync();
        var job = new OptimizeOldNewsImagesJob(NullLogger<OptimizeOldNewsImagesJob>.Instance, database,
            statusDatabase, Substitute.For<IBackgroundJobClient>());

        await job.Execute(operation.Id, CancellationToken.None);

        Assert.Equal(expected, (await database.VersionedPages.FindAsync(old.Id))!.LatestContent);
        Assert.Equal(original, (await database.VersionedPages.FindAsync(recent.Id))!.LatestContent);
        Assert.Equal(original, (await database.VersionedPages.FindAsync(draft.Id))!.LatestContent);
        var previous = await database.PageVersions.SingleAsync();
        Assert.Equal(old.Id, previous.PageId);
        Assert.Equal("Original comment", previous.EditComment);
        Assert.Equal(original, DiffGenerator.Default.ApplyDiff(expected, previous.DecodeDiffData()));
        Assert.Equal("Updated 2 image link(s) in 1 news post(s).", operation.ExtendedDescription);

        await job.Execute(operation.Id, CancellationToken.None);
        Assert.Equal(1, await database.PageVersions.CountAsync());
    }
}
