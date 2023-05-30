namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class ItemMovedLocationClearJobTests : IClassFixture<RealUnitTestDatabaseFixture>, IDisposable
{
    private const long ItemId1 = 55;
    private const long ItemId2 = 56;
    private const long ItemId3 = 57;
    private const long ItemId4 = 58;
    private const long ItemId5 = 59;

    private const string MovedFrom1 = "aPlace/There";
    private const string MovedFrom2 = "another/thing";
    private const string MovedFrom3 = "stuff";
    private const string MovedFrom4 = "here";
    private const string MovedFrom5 = "from/there";

    private readonly XunitLogger<ItemMovedLocationClearJob> logger;
    private readonly RealUnitTestDatabaseFixture fixture;

    public ItemMovedLocationClearJobTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<ItemMovedLocationClearJob>(output);
    }

    [Fact]
    public async Task ItemMovedLocationClear_ProperlyClearsOnlyOldMoves()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        await CreateDatabaseItems(database);
        await database.SaveChangesAsync();

        // Run the job
        var job = new ItemMovedLocationClearJob(logger, database);
        await job.Execute(CancellationToken.None);

        // The same hack as used in SessionCleanupJobTests doesn't work, so we need to actually read back the data
        // with raw SQL to see the changes applied correctly
        var item1NewMoved = await database.Database.SqlQuery<string?>(
            $"SELECT moved_from_location AS \"Value\" FROM storage_items WHERE id = {ItemId1}").FirstOrDefaultAsync();
        Assert.Equal(MovedFrom1, item1NewMoved);

        var item2NewMoved = await database.Database.SqlQuery<string?>(
            $"SELECT moved_from_location AS \"Value\" FROM storage_items WHERE id = {ItemId2}").FirstOrDefaultAsync();
        Assert.Null(item2NewMoved);

        var item3NewMoved = await database.Database.SqlQuery<string?>(
            $"SELECT moved_from_location AS \"Value\" FROM storage_items WHERE id = {ItemId3}").FirstOrDefaultAsync();
        Assert.Null(item3NewMoved);

        var item4NewMoved = await database.Database.SqlQuery<string?>(
            $"SELECT moved_from_location AS \"Value\" FROM storage_items WHERE id = {ItemId4}").FirstOrDefaultAsync();
        Assert.Equal(MovedFrom4, item4NewMoved);

        // TODO: should special items be cleared?
        var item5NewMoved = await database.Database.SqlQuery<string?>(
            $"SELECT moved_from_location AS \"Value\" FROM storage_items WHERE id = {ItemId5}").FirstOrDefaultAsync();
        Assert.Null(item5NewMoved);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static async Task CreateDatabaseItems(ApplicationDbContext database)
    {
        var expiredTime = DateTime.UtcNow - AppInfo.RemoveMovedFromInfoAfter * 2;
        var slightlyExpired = DateTime.UtcNow - AppInfo.RemoveMovedFromInfoAfter - TimeSpan.FromSeconds(1);
        var notExpired = DateTime.UtcNow - AppInfo.RemoveMovedFromInfoAfter + TimeSpan.FromMinutes(1);

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = ItemId1,
            Name = "Anything goes",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            MovedFromLocation = MovedFrom1,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = ItemId2,
            Name = "Second",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            MovedFromLocation = MovedFrom2,
            UpdatedAt = expiredTime,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = ItemId3,
            Name = "Third",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            MovedFromLocation = MovedFrom3,
            UpdatedAt = slightlyExpired,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = ItemId4,
            Name = "4",
            Ftype = FileType.Folder,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            MovedFromLocation = MovedFrom4,
            UpdatedAt = notExpired,
        });

        await database.StorageItems.AddAsync(new StorageItem
        {
            Id = ItemId5,
            Name = "5",
            Ftype = FileType.File,
            ReadAccess = FileAccess.User,
            WriteAccess = FileAccess.User,
            MovedFromLocation = MovedFrom5,
            Special = true,
            UpdatedAt = expiredTime,
        });
    }
}
