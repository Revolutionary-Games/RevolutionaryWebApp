namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class PurgeOldDeletedFilesJobTests : IDisposable
{
    private static readonly TimeSpan OldItemTime = AppInfo.DeleteTrashedItemsAfter * 2;
    private static readonly TimeSpan NotOldItemTime = AppInfo.DeleteTrashedItemsAfter / 2;
    private static readonly TimeSpan BarelyOldItemTime = AppInfo.DeleteTrashedItemsAfter + TimeSpan.FromSeconds(15);

    private readonly XunitLogger<PurgeOldDeletedFilesJob> logger;

    private readonly Random random = new();

    public PurgeOldDeletedFilesJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<PurgeOldDeletedFilesJob>(output);
    }

    [Fact]
    public async Task PurgeOldDeletedFilesJob_OldIsDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFilesJob_OldIsDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Name = random.Next().ToString(),
            Ftype = FileType.File,
            Deleted = true,
        };
        await CreateDummyDeleteInfo(database, item, DateTime.UtcNow - OldItemTime);
        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFilesJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        jobClientMock.Received().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task PurgeOldDeletedFilesJob_SlightlyTooOldIsDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFilesJob_SlightlyTooOldIsDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Name = random.Next().ToString(),
            Ftype = FileType.File,
            Deleted = true,
        };
        await CreateDummyDeleteInfo(database, item, DateTime.UtcNow - BarelyOldItemTime);
        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFilesJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        jobClientMock.Received().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task PurgeOldDeletedFilesJob_SpecialIsNotDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFilesJob_SpecialIsNotDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Name = random.Next().ToString(),
            Ftype = FileType.File,
            Deleted = true,
            Special = true,
        };
        await CreateDummyDeleteInfo(database, item, DateTime.UtcNow - OldItemTime);
        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFilesJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        // This approach is used to detect when the job wanted to delete something
        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task PurgeOldDeletedFilesJob_NonDeletedIsNotDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFilesJob_NonDeletedIsNotDeleted)).Options);

        var item = new StorageItem
        {
            Id = 12,
            Name = random.Next().ToString(),
            Ftype = FileType.File,
            Deleted = false,
            Special = true,
        };
        await CreateDummyDeleteInfo(database, item, DateTime.UtcNow - OldItemTime);
        await database.StorageItems.AddAsync(item);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFilesJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        Assert.False(item.Deleted);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    [Fact]
    public async Task PurgeOldDeletedFilesJob_NewItemIsNotDeleted()
    {
        var jobClientMock = Substitute.For<IBackgroundJobClient>();

        var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PurgeOldDeletedFilesJob_NewItemIsNotDeleted)).Options);

        var item1 = new StorageItem
        {
            Id = 12,
            Name = random.Next().ToString(),
            Ftype = FileType.File,
            Deleted = true,
        };
        await CreateDummyDeleteInfo(database, item1, DateTime.UtcNow - NotOldItemTime);
        await database.StorageItems.AddAsync(item1);

        var item2 = new StorageItem
        {
            Id = 13,
            Name = random.Next().ToString(),
            Ftype = FileType.File,
            Deleted = true,
        };
        await CreateDummyDeleteInfo(database, item2, DateTime.UtcNow);
        await database.StorageItems.AddAsync(item2);

        await database.SaveChangesAsync();

        var job = new PurgeOldDeletedFilesJob(logger, database, jobClientMock);

        await job.Execute(CancellationToken.None);

        Assert.Empty(jobClientMock.ReceivedCalls());
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private async Task CreateDummyDeleteInfo(ApplicationDbContext database, StorageItem item, DateTime deleteTime)
    {
        var deleteInfo = new StorageItemDeleteInfo(item, "original/path")
        {
            DeletedAt = deleteTime,
        };

        await database.StorageItemDeleteInfos.AddAsync(deleteInfo);
    }
}
