namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Jobs;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class CreateDefaultFolderJobTests : IClassFixture<RealUnitTestDatabaseFixture>, System.IDisposable
{
    private readonly XunitLogger<CreateDefaultFoldersJob> logger;
    private readonly RealUnitTestDatabaseFixture fixture;

    public CreateDefaultFolderJobTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<CreateDefaultFoldersJob>(output);
    }

    [Fact]
    public async Task CreateDefaultFolders_InCleanDatabase()
    {
        var clientMock = Substitute.For<IBackgroundJobClient>();

        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        Assert.Null(
            await database.StorageItems.FirstOrDefaultAsync(i => i.Name == "Trash" && i.ParentId == null));

        var instance = new CreateDefaultFoldersJob(logger, database, clientMock);

        await instance.Execute(CancellationToken.None);

        Assert.NotNull(
            await database.StorageItems.FirstOrDefaultAsync(i => i.Name == "Trash" && i.ParentId == null));

        clientMock.Received().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
