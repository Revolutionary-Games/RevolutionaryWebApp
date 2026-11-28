namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Jobs.Pages;
using Server.Models;
using Server.Models.Pages;
using Server.Services;
using Server.Utilities;
using Shared.Models.Pages;
using Shared.Utilities;
using Xunit;

public class PurgeMediaFileJobRealDbTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;

    public PurgeMediaFileJobRealDbTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(true, 89, 91)]
    [InlineData(false, 2, 4)]
    public async Task PurgeHonorsRetentionAndKeepsDatabaseRecordUntilAllStorageDeletesSucceed(
        bool processed, int tooRecentDays, int oldEnoughDays)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(fixture.GetConnectionStringForAdditionalDatabase()).UseSnakeCaseNamingConvention().Options;
        var notifications = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = new NotificationsEnabledDb(options, notifications);
        var deletedAt = DateTime.UtcNow.Date.AddDays(-tooRecentDays);
        var file = new MediaFile($"{Guid.NewGuid()}.png", Guid.NewGuid(), MediaFolder.WebsitePartsId, null)
        {
            Processed = processed,
            Deleted = true,
            DeletedAt = deletedAt,
        };
        await database.MediaFiles.AddAsync(file);
        await database.MediaFileUsages.AddAsync(new MediaFileUsage
        {
            MediaFile = file,
            Usage = MediaFileUsage.UsageType.Page,
            UsedByResource = -1,
        });
        await database.SaveChangesAsync();

        await using (var transaction = await database.Database.BeginTransactionAsync())
        {
            var unavailable = await MediaFileLifecycle.FindUnavailablePageMedia(database,
                $"![Example](media:png:{file.GlobalId})");
            Assert.Equal((file.GlobalId, file.Name), unavailable);
            await transaction.CommitAsync();
        }

        var mediaStorage = Substitute.For<IMediaStorage>();
        var uploadStorage = Substitute.For<IUploadFileStorage>();
        mediaStorage.Configured.Returns(true);
        uploadStorage.Configured.Returns(true);
        var job = new PurgeMediaFileJob(database, mediaStorage, uploadStorage, NullLogger<PurgeMediaFileJob>.Instance);

        await job.Execute(file.Id, deletedAt, false, CancellationToken.None);
        await mediaStorage.DidNotReceiveWithAnyArgs().DeleteObject(string.Empty);
        Assert.False(file.PurgeStarted);

        deletedAt = DateTime.UtcNow.Date.AddDays(-oldEnoughDays);
        file.DeletedAt = deletedAt;
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var webpPreview = MediaFileExtensions.GetViewPath(file.GlobalId.ToString(), MediaFileSize.FitPage, ".webp");
        mediaStorage.DeleteObject(webpPreview).Returns(Task.FromException(new IOException("Temporary storage error")));

        await Assert.ThrowsAsync<IOException>(() => job.Execute(file.Id, deletedAt, false, CancellationToken.None));
        Assert.True((await database.MediaFiles.AsNoTracking().SingleAsync(item => item.Id == file.Id)).PurgeStarted);
        Assert.True(await database.MediaFileUsages.AnyAsync(usage => usage.MediaFileGuid == file.GlobalId));

        mediaStorage.ClearReceivedCalls();
        mediaStorage.DeleteObject(webpPreview).Returns(Task.CompletedTask);
        await job.Execute(file.Id, deletedAt, false, CancellationToken.None);

        foreach (var size in Enum.GetValues<MediaFileSize>())
            await mediaStorage.Received(1).DeleteObject(MediaFileExtensions.GetStoragePath(file, size));

        await mediaStorage.Received(1).DeleteObject(webpPreview);
        await mediaStorage.Received(1).DeleteObject($"{file.GlobalId}/thumb.png");
        var legacyBase = $"{file.GlobalId}/{Path.GetFileNameWithoutExtension(file.Name)}";
        await mediaStorage.Received(1).DeleteObject($"{legacyBase}_thumb.png");
        await uploadStorage.Received(1).DeleteObject(file.GetUploadPath());
        await uploadStorage.Received(1).DeleteObject(file.GetIntermediateProcessingPath());
        Assert.False(await database.MediaFiles.AnyAsync(item => item.Id == file.Id));
        Assert.False(await database.MediaFileUsages.AnyAsync(usage => usage.MediaFileGuid == file.GlobalId));
    }
}
