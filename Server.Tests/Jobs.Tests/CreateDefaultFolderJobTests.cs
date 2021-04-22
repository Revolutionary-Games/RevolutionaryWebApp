namespace ThriveDevCenter.Server.Tests.Jobs.Tests
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Fixtures;
    using Hangfire;
    using Hangfire.Common;
    using Hangfire.States;
    using Moq;
    using Server.Jobs;
    using Utilities;
    using Xunit;
    using Xunit.Abstractions;

    public class CreateDefaultFolderJobTests
    {
        public class RecomputeHashedColumnsTests : IClassFixture<RealUnitTestDatabaseFixture>
        {
            private readonly XunitLogger<CreateDefaultFoldersJob> logger;
            private readonly RealUnitTestDatabaseFixture fixture;

            public RecomputeHashedColumnsTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
            {
                this.fixture = fixture;
                logger = new XunitLogger<CreateDefaultFoldersJob>(output);
            }

            [Fact]
            public async Task CreateDefaultFolders_InCleanDatabase()
            {
                var clientMock = new Mock<IBackgroundJobClient>();
                clientMock.Setup((client) => client.Create(It.IsAny<Job>(), It.IsAny<EnqueuedState>())).Verifiable();

                var database = fixture.Database;
                await using var transaction = await database.Database.BeginTransactionAsync();

                Assert.Null(
                    await database.StorageItems.FirstOrDefaultAsync(i => i.Name == "Trash" && i.ParentId == null));

                var instance = new CreateDefaultFoldersJob(logger, database, clientMock.Object);

                await instance.Execute(CancellationToken.None);

                Assert.NotNull(
                    await database.StorageItems.FirstOrDefaultAsync(i => i.Name == "Trash" && i.ParentId == null));

                clientMock.Verify();
            }
        }
    }
}
