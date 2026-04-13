namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Common.Models;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared.Models;
using Shared.Models.Enums;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

/// <summary>
///   Real DB concurrency test for runner/job coordination. Is similar to RunnerCommunicationTests but
///   with a PostgreSQL-backed test database to ensure concurrent job starts are handled safely.
/// </summary>
public sealed class RunnerCommunicationRealDbTests : IClassFixture<Fixtures.RealUnitTestDatabaseFixture>, IDisposable
{
    private readonly Fixtures.RealUnitTestDatabaseFixture fixture;
    private readonly XunitLogger<RunnerConnectionHandler> logger;

    public RunnerCommunicationRealDbTests(ITestOutputHelper output, Fixtures.RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
        logger = new XunitLogger<RunnerConnectionHandler>(output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunnerCommunication_RealDbConcurrentStartErrorHandling(bool reAskJobs)
    {
        // Unique tag to isolate from other tests
        var uniqueTag = $"real-db-concurrent-{Guid.NewGuid():N}";

        // Build NotificationsEnabledDb contexts using the real DB connection string
        var connectionString = fixture.GetConnectionStringForAdditionalDatabase();

        NotificationsEnabledDb MakeDb()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options;

            var notifier = Substitute.For<IModelUpdateNotificationSender>();
            return new NotificationsEnabledDb(options, notifier);
        }

        // Seed CI project, build, jobs and CI image
        var seedingDb = fixture.Database;

        var project = new CiProject
        {
            Name = $"RealDB-Concurrency-{uniqueTag}",
            Enabled = true,
        };
        await seedingDb.CiProjects.AddAsync(project);
        await seedingDb.SaveChangesAsync();

        var build = new CiBuild { CiProjectId = project.Id };
        await seedingDb.CiBuilds.AddAsync(build);
        await seedingDb.SaveChangesAsync();

        // Ensure the CI image exists so that job starting works
        var imageName = $"test-image:{uniqueTag}";
        await RunnerConnectionMockHelper.CreateBasicCIImageAsync(seedingDb, imageName);

        // Minimal valid cache configuration for jobs
        string testCacheSettings = JsonSerializer.Serialize(new CiJobCacheConfiguration
        {
            LoadFrom = ["cache-a"],
            WriteTo = "cache-b",
        });

        var job1 = new CiJob
        {
            CiProjectId = project.Id,
            CiBuildId = build.CiBuildId,
            CiJobId = 1,
            CacheSettingsJson = testCacheSettings,
            RequiredRunnerTags = uniqueTag,
            Image = imageName,
        };
        var job2 = new CiJob
        {
            CiProjectId = project.Id,
            CiBuildId = build.CiBuildId,
            CiJobId = 2,
            CacheSettingsJson = testCacheSettings,
            RequiredRunnerTags = uniqueTag,
            Image = imageName,
        };
        var job3 = new CiJob
        {
            CiProjectId = project.Id,
            CiBuildId = build.CiBuildId,
            CiJobId = 3,
            CacheSettingsJson = testCacheSettings,
            RequiredRunnerTags = uniqueTag,
            Image = imageName,
        };

        await seedingDb.CiJobs.AddRangeAsync(job1, job2, job3);
        await seedingDb.SaveChangesAsync();

        // Create two independent runner connections using separate contexts to simulate separate server connections
        var dbForRunner1 = MakeDb();
        var dbForRunner2 = MakeDb();

        // Create the runners in a transaction so that their settings will apply at the same time
        await dbForRunner1.Database.BeginTransactionAsync();
        await dbForRunner2.Database.BeginTransactionAsync();

        var connection1 = await RunnerConnectionMockHelper.Create(
            nameof(RunnerCommunication_RealDbConcurrentStartErrorHandling),
            logger, existingDatabase: dbForRunner1);
        var connection2 = await RunnerConnectionMockHelper.Create(
            nameof(RunnerCommunication_RealDbConcurrentStartErrorHandling),
            logger, existingDatabase: dbForRunner2);

        // Tag the runners so they see only our tagged jobs. Not the cleanest but will do for just this one test
        // needing this
        connection1.GetRunnerData().Tags = uniqueTag;
        await dbForRunner1.SaveChangesAsync();
        await dbForRunner1.Database.CommitTransactionAsync();
        connection2.GetRunnerData().Tags = uniqueTag;
        await dbForRunner2.SaveChangesAsync();
        await dbForRunner2.Database.CommitTransactionAsync();

        // Data setup is now done
        Assert.True(await connection1.Start());
        Assert.True(await connection2.Start());

        await connection1.WaitDequeueAuthRequest();
        await connection2.WaitDequeueAuthRequest();

        // Both request available jobs (as close to each other as possible)
        connection1.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });
        connection2.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });

        var listMsg1 = await connection1.WaitForServerMessage();
        var listMsg2 = await connection2.WaitForServerMessage();
        Assert.NotNull(listMsg1);
        Assert.NotNull(listMsg2);
        Assert.Equal(BuildSectionMessageType.JobsList, listMsg1.Type);
        Assert.Equal(BuildSectionMessageType.JobsList, listMsg2.Type);

        var jobsList1 = JsonSerializer.Deserialize<AvailableJobsList>(listMsg1.Output ?? string.Empty);
        var jobsList2 = JsonSerializer.Deserialize<AvailableJobsList>(listMsg2.Output ?? string.Empty);
        Assert.NotNull(jobsList1);
        Assert.NotNull(jobsList2);
        Assert.True(jobsList1.Jobs.Count >= 1);
        Assert.True(jobsList2.Jobs.Count >= 1);

        // Both should see the same job at the top of the list (job1)
        Assert.Equal(job1.CiProjectId, jobsList1.Jobs[0].CiProjectId);
        Assert.Equal(job1.CiBuildId, jobsList1.Jobs[0].CiBuildId);
        Assert.Equal(job1.CiJobId, jobsList1.Jobs[0].CiJobId);
        Assert.Equal(jobsList1.Jobs[0].CiProjectId, jobsList2.Jobs[0].CiProjectId);
        Assert.Equal(jobsList1.Jobs[0].CiBuildId, jobsList2.Jobs[0].CiBuildId);
        Assert.Equal(jobsList1.Jobs[0].CiJobId, jobsList2.Jobs[0].CiJobId);

        // Both clients should have received the exact same lists
        Assert.Equal(listMsg1.Output, listMsg2.Output);

        // Race: both attempt to start the exact same job
        var jobIdTriplet = $"{job1.CiProjectId}:{job1.CiBuildId}:{job1.CiJobId}";
        var message1 = new RealTimeBuildMessage
            { Type = BuildSectionMessageType.RequestStartJob, Output = jobIdTriplet };
        var message2 = new RealTimeBuildMessage
            { Type = BuildSectionMessageType.RequestStartJob, Output = jobIdTriplet };

        // Run a thread for the other start to attempt to get them to hit as close as possible to the same time
        {
            var thread = new Thread(() => connection2.QueueMessage(message2));
            thread.Start();
            connection1.QueueMessage(message1);
            thread.Join();
        }

        var startResp1 = await connection1.WaitForServerMessage();
        var startResp2 = await connection2.WaitForServerMessage();
        Assert.NotNull(startResp1);
        Assert.NotNull(startResp2);

        // Exactly one should succeed in reserving the job; the other should receive an error
        int successes = 0;
        int errors = 0;
        if (startResp1.Type == BuildSectionMessageType.ActiveJobDetails)
            ++successes;
        if (startResp2.Type == BuildSectionMessageType.ActiveJobDetails)
            ++successes;
        if (startResp1.Type == BuildSectionMessageType.Error)
            ++errors;
        if (startResp2.Type == BuildSectionMessageType.Error)
            ++errors;

        Assert.Equal(1, successes);
        Assert.Equal(1, errors);

        // Identify winner/loser connections
        var winner = startResp1.Type == BuildSectionMessageType.ActiveJobDetails ? connection1 : connection2;
        var loser = startResp1.Type == BuildSectionMessageType.ActiveJobDetails ? connection2 : connection1;

        // Loser requests jobs again and starts the next one (job2)
        loser.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });
        var loserList = await loser.WaitForServerMessage();
        Assert.NotNull(loserList);
        Assert.Equal(BuildSectionMessageType.JobsList, loserList.Type);
        var loserJobs = JsonSerializer.Deserialize<AvailableJobsList>(loserList.Output ?? string.Empty);
        Assert.NotNull(loserJobs);
        Assert.True(loserJobs.Jobs.Count >= 1);
        Assert.True(loserJobs.Jobs.Count < jobsList1.Jobs.Count);

        // The first available job should now be job2 as the winner reserves job1
        Assert.Equal(job2.CiJobId, loserJobs.Jobs[0].CiJobId);

        var job2Triplet = $"{job2.CiProjectId}:{job2.CiBuildId}:{job2.CiJobId}";
        var job2RequestStartMessage = new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.RequestStartJob,
            Output = job2Triplet,
        };

        loser.QueueMessage(job2RequestStartMessage);
        var loserStart = await loser.WaitForServerMessage();
        Assert.NotNull(loserStart);
        int attempts = 0;
        while (loserStart.Type == BuildSectionMessageType.Error && attempts < 3)
        {
            // Accept a transient failure and retry
            Assert.True(
                loserStart.ErrorMessage is "Could not start working on the job (some other runner probably took it)"
                    or "You already have a job reserved, please work on it or abandon it");

            if (reAskJobs)
            {
                // It is not mandatory to request a new list on failure, but it is allowed to do so
                loser.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });
                var retryList = await loser.WaitForServerMessage();
                Assert.NotNull(retryList);
                Assert.Equal(BuildSectionMessageType.JobsList, retryList.Type);
                var retryJobs = JsonSerializer.Deserialize<AvailableJobsList>(retryList.Output ?? string.Empty);
                Assert.NotNull(retryJobs);
                Assert.True(retryJobs.Jobs.Count >= 1);

                var next = retryJobs.Jobs.First();
                loser.QueueMessage(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.RequestStartJob,
                    Output = $"{next.CiProjectId}:{next.CiBuildId}:{next.CiJobId}",
                });
            }
            else
            {
                // We are sure in the test that job2 is fine, so keep asking for it. A real runner would move onto
                // the next job in the list it was offered, but we don't need to do that here.
                loser.QueueMessage(job2RequestStartMessage);
            }

            loserStart = await loser.WaitForServerMessage();
            Assert.NotNull(loserStart);
            ++attempts;
        }

        Assert.Equal(BuildSectionMessageType.ActiveJobDetails, loserStart.Type);

        // Both connections now have an active job; finish them successfully
        winner.QueueMessage(new RealTimeBuildMessage
            { Type = BuildSectionMessageType.FinalStatus, WasSuccessful = true });
        loser.QueueMessage(
            new RealTimeBuildMessage { Type = BuildSectionMessageType.FinalStatus, WasSuccessful = true });

        // Ask for jobs again to ensure processing completed without server errors
        winner.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });
        loser.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });

        var postFinish1 = await winner.WaitForServerMessage();
        var postFinish2 = await loser.WaitForServerMessage();
        Assert.NotNull(postFinish1);
        Assert.NotNull(postFinish2);
        Assert.Equal(BuildSectionMessageType.JobsList, postFinish1.Type);
        Assert.NotNull(postFinish1.Output);
        Assert.Equal(BuildSectionMessageType.JobsList, postFinish2.Type);

        // The last job's data should be the exact same across the messages
        Assert.Equal(postFinish1.Output, postFinish2.Output);

        // Close sockets
        connection1.QueueCloseMessage();
        connection2.QueueCloseMessage();
        await connection1.WaitUntilClosed();
        await connection2.WaitUntilClosed();

        // Verify final states by reloading from a fresh context
        var verifyDb = MakeDb();
        var reloadedJobs = await verifyDb.CiJobs
            .Where(j => j.CiProjectId == project.Id && j.CiBuildId == build.CiBuildId)
            .OrderBy(j => j.CiJobId).ToListAsync();

        Assert.Equal(3, reloadedJobs.Count);

        var rj1 = reloadedJobs[0];
        var rj2 = reloadedJobs[1];
        var rj3 = reloadedJobs[2];

        // Job1 and Job2 should be finished successfully; Job3 should remain not started
        Assert.Equal(CIJobState.Finished, rj1.State);
        Assert.True(rj1.Succeeded);
        Assert.NotNull(rj1.FinishedAt);
        Assert.Null(rj1.ReservedByRunnerId);

        Assert.Equal(CIJobState.Finished, rj2.State);
        Assert.True(rj2.Succeeded);
        Assert.NotNull(rj2.FinishedAt);
        Assert.Null(rj2.ReservedByRunnerId);

        Assert.Equal(CIJobState.Starting, rj3.State);
        Assert.Null(rj3.FinishedAt);
        Assert.Null(rj3.ReservedByRunnerId);
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
