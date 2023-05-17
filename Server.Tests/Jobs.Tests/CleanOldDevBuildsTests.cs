namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Moq;
using Server.Jobs;
using Server.Models;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class CleanOldDevBuildsTests : IClassFixture<RealUnitTestDatabaseFixture>, IDisposable
{
    // old build 1
    private const long Build1Id = 12020;
    private const string Build1Hash = "4355453465";

    // old build 2
    private const long Build2Id = 12021;
    private const string Build2Hash = "5434546463";

    // old build 3 (keep)
    private const long Build3Id = 12022;
    private const string Build3Hash = "57462346";

    // old build 4 (botd)
    private const long Build4Id = 12023;
    private const string Build4Hash = "434554";

    // new build 1
    private const long Build5Id = 12024;
    private const string Build5Hash = "2357586";

    // new build 2 (keep)
    private const long Build6Id = 12025;
    private const string Build6Hash = "7685345";

    // Dehydrated objects unique to each build
    private const long DehydratedId1 = 99120;
    private const long DehydratedId2 = 99121;
    private const long DehydratedId3 = 99122;
    private const long DehydratedId4 = 99123;
    private const long DehydratedId5 = 99124;
    private const long DehydratedId6 = 99125;

    // Shared dehydrated
    // Shared between build 1 and 2
    private const long DehydratedSharedId1 = 99126;
    private const string DehydratedSharedHash1 = "ae45453345";

    // Shared between all builds
    private const long DehydratedSharedId2 = 99127;
    private const string DehydratedSharedHash2 = "ae546345";

    // Shared between build 1 and 6
    private const long DehydratedSharedId3 = 99128;
    private const string DehydratedSharedHash3 = "ae476945";

    // Shared between build 1 and 3
    private const long DehydratedSharedId4 = 99129;
    private const string DehydratedSharedHash4 = "ae123879";

    // Shared between build 1, 2 and 5
    private const long DehydratedSharedId5 = 99130;
    private const string DehydratedSharedHash5 = "ae12432";

    // Shared between build 2 and 5
    private const long DehydratedSharedId6 = 99131;
    private const string DehydratedSharedHash6 = "ae454512389";

    private static readonly TimeSpan Build1TimeAgo = AppInfo.UnimportantDevBuildKeepDuration * 2;
    private static readonly TimeSpan Build2TimeAgo = AppInfo.UnimportantDevBuildKeepDuration * 1.5f;
    private static readonly TimeSpan Build3TimeAgo = AppInfo.UnimportantDevBuildKeepDuration * 2;
    private static readonly TimeSpan Build4TimeAgo = AppInfo.UnimportantDevBuildKeepDuration * 2;
    private static readonly TimeSpan Build5TimeAgo = AppInfo.UnimportantDevBuildKeepDuration * 0.5f;
    private static readonly TimeSpan Build6TimeAgo = AppInfo.UnimportantDevBuildKeepDuration * 0.1f;

    private readonly XunitLogger<CleanOldDevBuildsJob> logger;
    private readonly XunitLogger<DeleteUnneededDehydratedObjectsJob> logger2;
    private readonly RealUnitTestDatabaseFixture fixture;

    private readonly Random random = new();

    public CleanOldDevBuildsTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<CleanOldDevBuildsJob>(output);
        logger2 = new XunitLogger<DeleteUnneededDehydratedObjectsJob>(output);
    }

    [Fact]
    public async Task DevBuildClean_RightBuildsAndDehydratedGetDeleted()
    {
        var clientMock = new Mock<IBackgroundJobClient>();
        clientMock.Setup(client => client.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>())).Verifiable();

        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        await SetupDevBuildsInDb();

        Assert.NotNull(await database.DevBuilds.FindAsync(Build1Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build2Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build3Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build4Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build5Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build6Id));

        var instance = new CleanOldDevBuildsJob(logger, database, clientMock.Object);

        await instance.Execute(CancellationToken.None);

        Assert.Null(await database.DevBuilds.FindAsync(Build1Id));
        Assert.Null(await database.DevBuilds.FindAsync(Build2Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build3Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build4Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build5Id));
        Assert.NotNull(await database.DevBuilds.FindAsync(Build6Id));

        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId1));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId2));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId3));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId4));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId5));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId6));

        clientMock.Verify();

        var dehydratedClean = new DeleteUnneededDehydratedObjectsJob(logger2, database, clientMock.Object);

        await dehydratedClean.Execute(CancellationToken.None);

        Assert.Null(await database.DehydratedObjects.FindAsync(DehydratedId1));
        Assert.Null(await database.DehydratedObjects.FindAsync(DehydratedId2));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId3));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId4));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId5));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedId6));

        Assert.Null(await database.DehydratedObjects.FindAsync(DehydratedSharedId1));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedSharedId2));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedSharedId3));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedSharedId4));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedSharedId5));
        Assert.NotNull(await database.DehydratedObjects.FindAsync(DehydratedSharedId6));

        clientMock.Verify();
    }

    public void Dispose()
    {
        logger.Dispose();
        logger2.Dispose();
    }

    private async Task SetupDevBuildsInDb()
    {
        var build1 = await CreateBuild(Build1Id, Build1Hash, DehydratedId1, false, false, Build1TimeAgo);
        var build2 = await CreateBuild(Build2Id, Build2Hash, DehydratedId2, false, false, Build2TimeAgo);
        var build3 = await CreateBuild(Build3Id, Build3Hash, DehydratedId3, false, true, Build3TimeAgo);
        var build4 = await CreateBuild(Build4Id, Build4Hash, DehydratedId4, true, false, Build4TimeAgo);

        var build5 = await CreateBuild(Build5Id, Build5Hash, DehydratedId5, false, false, Build5TimeAgo);
        var build6 = await CreateBuild(Build6Id, Build6Hash, DehydratedId6, false, true, Build6TimeAgo);

        // Shared dehydrated objects
        var sharedDehydrated1 = await CreateDehydratedObject(DehydratedSharedId1, DehydratedSharedHash1);
        build1.DehydratedObjects.Add(sharedDehydrated1);
        build2.DehydratedObjects.Add(sharedDehydrated1);

        var sharedDehydrated2 = await CreateDehydratedObject(DehydratedSharedId2, DehydratedSharedHash2);
        build1.DehydratedObjects.Add(sharedDehydrated2);
        build2.DehydratedObjects.Add(sharedDehydrated2);
        build3.DehydratedObjects.Add(sharedDehydrated2);
        build4.DehydratedObjects.Add(sharedDehydrated2);
        build5.DehydratedObjects.Add(sharedDehydrated2);
        build6.DehydratedObjects.Add(sharedDehydrated2);

        var sharedDehydrated3 = await CreateDehydratedObject(DehydratedSharedId3, DehydratedSharedHash3);
        build1.DehydratedObjects.Add(sharedDehydrated3);
        build6.DehydratedObjects.Add(sharedDehydrated3);

        var sharedDehydrated4 = await CreateDehydratedObject(DehydratedSharedId4, DehydratedSharedHash4);
        build1.DehydratedObjects.Add(sharedDehydrated4);
        build3.DehydratedObjects.Add(sharedDehydrated4);

        var sharedDehydrated5 = await CreateDehydratedObject(DehydratedSharedId5, DehydratedSharedHash5);
        build1.DehydratedObjects.Add(sharedDehydrated5);
        build2.DehydratedObjects.Add(sharedDehydrated5);
        build5.DehydratedObjects.Add(sharedDehydrated5);

        var sharedDehydrated6 = await CreateDehydratedObject(DehydratedSharedId6, DehydratedSharedHash6);
        build2.DehydratedObjects.Add(sharedDehydrated6);
        build5.DehydratedObjects.Add(sharedDehydrated6);

        await fixture.Database.SaveChangesAsync();
    }

    private async Task<DevBuild> CreateBuild(long id, string hash, long uniqueDehydratedId, bool botd, bool keep,
        TimeSpan timeAgo)
    {
        var time = DateTime.UtcNow - timeAgo;

        var build = new DevBuild
        {
            Id = id,
            BuildHash = hash,
            Platform = "Linux/X11",
            Branch = "master",
            BuildZipHash = hash,
            BuildOfTheDay = botd,
            Keep = keep,
            StorageItem = await fixture.CreateTestStorageItem(true, random),
            CreatedAt = time,
            UpdatedAt = time,
        };

        build.DehydratedObjects.Add(await CreateDehydratedObject(uniqueDehydratedId, $"dummy:sha:{hash}"));

        await fixture.Database.DevBuilds.AddAsync(build);

        return build;
    }

    private async Task<DehydratedObject> CreateDehydratedObject(long id, string sha3)
    {
        var dehydrated = new DehydratedObject
        {
            Id = id,
            Sha3 = sha3,
            StorageItem = await fixture.CreateTestStorageItem(true, random),
        };

        await fixture.Database.DehydratedObjects.AddAsync(dehydrated);

        return dehydrated;
    }
}
