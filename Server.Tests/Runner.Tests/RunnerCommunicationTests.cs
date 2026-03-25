namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Common.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using Server.Controllers;
using Server.Models;
using Shared.Models;
using Shared.Models.Enums;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class RunnerCommunicationTests(ITestOutputHelper output) : IDisposable
{
    private readonly XunitLogger<RunnerConnectionHandler> logger = new(output);

    [Fact]
    public async Task Runner_CanConnectWithWebsocket()
    {
        var listenerMockSetup = await RunnerConnectionMockHelper.Create(nameof(Runner_CanConnectWithWebsocket), logger);

        // Let's also send a HeartBeat message to see it gets handled
        listenerMockSetup.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.HeartBeat });

        // And then close the connection
        listenerMockSetup.QueueCloseMessage();

        Assert.Equal(2, listenerMockSetup.GetQueuedMessageCount());

        // Start things up
        Assert.True(await listenerMockSetup.Start());

        // Verify that the socket was accepted and some messages were exchanged
        await listenerMockSetup.CheckAuthHappened();

        await listenerMockSetup.WaitUntilClosed();

        Assert.Equal(0, listenerMockSetup.GetQueuedMessageCount());
    }

    [Fact]
    public async Task Runner_WrongKeyDisallowsWebSocket()
    {
        var listenerMockSetup =
            await RunnerConnectionMockHelper.Create(nameof(Runner_WrongKeyDisallowsWebSocket), logger);

        listenerMockSetup.QueueCloseMessage();

        var dummyContext = new DefaultHttpContext
        {
            Request =
            {
                Query = new QueryCollection(new Dictionary<string, StringValues>
                {
                    { "runnerId", "1234" },
                }),
            },
        };

        Assert.False(await listenerMockSetup.Start(false, dummyContext));

        await listenerMockSetup.CheckSocketWasNotAccepted();

        await listenerMockSetup.WaitUntilClosed();

        Assert.Equal(1, listenerMockSetup.GetQueuedMessageCount());
    }

    [Fact]
    public async Task Runner_WrongSecretAbortsSocket()
    {
        var listenerMockSetup = await RunnerConnectionMockHelper.Create(nameof(Runner_WrongSecretAbortsSocket), logger);

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage
            { Type = BuildSectionMessageType.AuthResponse, Output = "1234" });
        listenerMockSetup.QueueCloseMessage();

        Assert.Equal(2, listenerMockSetup.GetQueuedMessageCount());
        Assert.False(await listenerMockSetup.Start(false));

        await listenerMockSetup.WaitUntilClosed();

        // One message needs to be read from the socket as the server should read the bad auth and then close the socket
        Assert.Equal(1, listenerMockSetup.GetQueuedMessageCount());

        Assert.True(listenerMockSetup.TryDequeueServerMessage(out var serverMessage, out _));
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.AuthDemand, serverMessage.Type);

        // Server sent an error about the incorrect key
        Assert.True(listenerMockSetup.TryDequeueServerMessage(out serverMessage, out _));
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.Error, serverMessage.Type);

        Assert.False(listenerMockSetup.TryDequeueServerMessage(out serverMessage, out _));
    }

    [Fact]
    public async Task Runner_CannotSendOutputWithoutJob()
    {
        var listenerMockSetup = await RunnerConnectionMockHelper.Create(nameof(Runner_CanConnectWithWebsocket), logger);

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.HeartBeat });

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage
            { Type = BuildSectionMessageType.SectionStart, SectionId = 1, SectionName = "Example section" });

        // And then close the connection
        listenerMockSetup.QueueCloseMessage();

        Assert.True(await listenerMockSetup.Start());

        await listenerMockSetup.CheckAuthHappened();

        await listenerMockSetup.WaitUntilClosed();

        Assert.Equal(0, listenerMockSetup.GetQueuedMessageCount());

        listenerMockSetup.DequeueAuthRequest();

        Assert.True(listenerMockSetup.TryDequeueServerMessage(out var serverMessage, out _));
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.Error, serverMessage.Type);

        Assert.Contains("No active job", serverMessage.ErrorMessage);
        Assert.Contains("required for this message", serverMessage.ErrorMessage);
    }

    [Fact]
    public async Task Runner_CanAskAboutJobs()
    {
        var listenerMockSetup = await RunnerConnectionMockHelper.Create(nameof(Runner_CanAskAboutJobs), logger);

        var db = listenerMockSetup.AccessDatabase();

        var sampleProject1 = new CiProject
        {
            Name = "Sample project",
            Enabled = true,
        };
        await db.CiProjects.AddAsync(sampleProject1);

        var sampleBuild1 = new CiBuild
        {
            CiProject = sampleProject1,
        };

        await db.CiBuilds.AddAsync(sampleBuild1);
        await db.SaveChangesAsync();

        var sampleJob1 = new CiJob
        {
            CiProjectId = sampleProject1.Id,
            CiBuildId = sampleBuild1.CiBuildId,
            CiJobId = 12,
        };
        await db.CiJobs.AddAsync(sampleJob1);
        await db.SaveChangesAsync();

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });

        listenerMockSetup.QueueCloseMessage();

        Assert.Null(sampleJob1.ReservedByRunner);
        Assert.True(await listenerMockSetup.Start());

        await listenerMockSetup.WaitUntilClosed();

        listenerMockSetup.DequeueAuthRequest();

        Assert.True(listenerMockSetup.TryDequeueServerMessage(out var serverMessage, out _));
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.JobsList, serverMessage.Type);

        Assert.NotNull(serverMessage.Output);
        var data = JsonSerializer.Deserialize<AvailableJobsList>(serverMessage.Output);

        Assert.NotNull(data);
        Assert.Single(data.Jobs);
        Assert.Equal(JsonSerializer.Serialize(sampleJob1.GetDTO()), JsonSerializer.Serialize(data.Jobs[0]));
    }

    /// <summary>
    ///   This is an overall test of the entire system of getting jobs, picking one, "running" it and producing output
    ///   and then finishing it.
    /// </summary>
    [Fact]
    public async Task Runner_PickingAJobAndRunningIt()
    {
        string testCacheSettings = JsonSerializer.Serialize(new CiJobCacheConfiguration
        {
            LoadFrom = ["test1", "a"],
            WriteTo = "test2",
            Shared = new Dictionary<string, string>
            {
                { "a", "b" },
            },
            System = new Dictionary<string, string>
            {
                { "c", "d" },
            },
        });

        var listenerMockSetup = await RunnerConnectionMockHelper.Create(nameof(Runner_PickingAJobAndRunningIt), logger);

        var db = listenerMockSetup.AccessDatabase();

        var sampleProject1 = new CiProject
        {
            Name = "Sample project",
            Enabled = true,
        };
        await db.CiProjects.AddAsync(sampleProject1);

        var sampleBuild1 = new CiBuild
        {
            CiProject = sampleProject1,
        };

        await db.CiBuilds.AddAsync(sampleBuild1);
        await db.SaveChangesAsync();

        var sampleJob1 = new CiJob
        {
            CiProjectId = sampleProject1.Id,
            CiBuildId = sampleBuild1.CiBuildId,
            CiJobId = 12,
            CacheSettingsJson = testCacheSettings,
        };
        await db.CiJobs.AddAsync(sampleJob1);

        var sampleJob2 = new CiJob
        {
            CiProjectId = sampleProject1.Id,
            CiBuildId = sampleBuild1.CiBuildId,
            CiJobId = 13,
        };

        await db.CiJobs.AddAsync(sampleJob2);
        await db.SaveChangesAsync();

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });

        Assert.True(await listenerMockSetup.Start());

        await listenerMockSetup.WaitDequeueAuthRequest();

        Assert.Null(sampleJob1.ReservedByRunner);

        var serverMessage = await listenerMockSetup.WaitForServerMessage();
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.JobsList, serverMessage.Type);

        Assert.NotNull(serverMessage.Output);
        var data = JsonSerializer.Deserialize<AvailableJobsList>(serverMessage.Output);
        Assert.NotNull(data);
        Assert.Equal(2, data.Jobs.Count);

        // Make sure the server told us about the job
        bool hadJob1 = false;

        foreach (var item in data.Jobs)
        {
            if (item.CiProjectId == sampleProject1.Id && item.CiBuildId == sampleBuild1.CiBuildId &&
                item.CiJobId == sampleJob1.CiJobId)
            {
                hadJob1 = true;
                break;
            }
        }

        if (!hadJob1)
            Assert.Fail("Server didn't tell us about job 1");

        Assert.Null(sampleJob1.ReservedByRunnerId);

        // Then we can request the job
        listenerMockSetup.QueueMessage(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.RequestStartJob,
            Output = $"{sampleProject1.Id}:{sampleBuild1.CiBuildId}:{sampleJob1.CiJobId}",
        });

        serverMessage = await listenerMockSetup.WaitForServerMessage();
        Assert.NotNull(serverMessage);

        Assert.Equal(BuildSectionMessageType.ActiveJobDetails, serverMessage.Type);
        Assert.NotNull(serverMessage.Output);
        var jobData = JsonSerializer.Deserialize<RunningJobDetails>(serverMessage.Output);
        Assert.NotNull(jobData);

        Assert.Equal(JsonSerializer.Serialize(sampleJob1.GetDTO()), JsonSerializer.Serialize(jobData.GeneralDetails));
        Assert.Equal(testCacheSettings, JsonSerializer.Serialize(jobData.CacheConfiguration));

        // We got the job so now do some test output
        listenerMockSetup.QueueMessage(new RealTimeBuildMessage
            { Type = BuildSectionMessageType.SectionStart, SectionId = 1, SectionName = "Example section" });
        listenerMockSetup.QueueMessage(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.BuildOutput, SectionId = 1, SectionName = "Example section",
            Output = "This is a test message that should go into a section",
        });
        listenerMockSetup.QueueMessage(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.SectionEnd, SectionId = 1, SectionName = "Example section",
            WasSuccessful = true,
        });

        // We should reserve Job while running
        Assert.NotNull(sampleJob1.ReservedByRunnerId);
        Assert.Equal(CIJobState.Running, sampleJob1.State);

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage
            { Type = BuildSectionMessageType.FinalStatus, WasSuccessful = true });

        // Done with the job, so confirm now that we could start a new job if we wanted to
        listenerMockSetup.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });

        serverMessage = await listenerMockSetup.WaitForServerMessage();
        Assert.NotNull(serverMessage);

        // This will detect if we ran into any errors with the above things
        Assert.Equal(BuildSectionMessageType.JobsList, serverMessage.Type);

        Assert.NotNull(serverMessage.Output);
        data = JsonSerializer.Deserialize<AvailableJobsList>(serverMessage.Output);
        Assert.NotNull(data);

        // Now returns only the non-finished job
        Assert.Single(data.Jobs);

        Assert.Equal(JsonSerializer.Serialize(sampleJob2.GetDTO()), JsonSerializer.Serialize(data.Jobs[0]));

        Assert.False(listenerMockSetup.TryDequeueServerMessage(out _, out _));

        // We fetch last database data before closing the listener as it will dispose the DB instance we gave it
        // Check that the output actually reached the database
        var sections = await db.CiJobOutputSections.Where(s =>
            s.CiProjectId == sampleJob1.CiProjectId && s.CiBuildId == sampleJob1.CiBuildId &&
            s.CiJobId == sampleJob1.CiJobId).ToListAsync();

        // Close finally
        listenerMockSetup.QueueCloseMessage();
        await listenerMockSetup.WaitUntilClosed();

        Assert.Single(sections);
        Assert.Equal("Example section", sections[0].Name);
        Assert.Equal("This is a test message that should go into a section", sections[0].Output);
        Assert.NotNull(sections[0].FinishedAt);
        Assert.Equal(CIJobSectionStatus.Succeeded, sections[0].Status);

        // And that the job status was updated on finish
        Assert.Null(sampleJob1.ReservedByRunnerId);
        Assert.NotNull(sampleJob1.FinishedAt);
        Assert.NotNull(sampleJob1.RanOnServer);
        Assert.NotNull(sampleJob1.TimeWaitingForServer);
        Assert.False(sampleJob1.OutputPurged);
        Assert.True(sampleJob1.Succeeded);
        Assert.Equal(CIJobState.Finished, sampleJob1.State);

        // And job 2 wasn't touched
        Assert.Null(sampleJob2.ReservedByRunnerId);
        Assert.Equal(CIJobState.Starting, sampleJob2.State);
    }

    [Fact]
    public async Task Runner_CannotAccessJobsWithoutAuth()
    {
        var listenerMockSetup =
            await RunnerConnectionMockHelper.Create(nameof(Runner_CannotAccessJobsWithoutAuth), logger);

        var db = listenerMockSetup.AccessDatabase();

        var sampleProject1 = new CiProject
        {
            Name = "Sample project",
            Enabled = true,
        };
        await db.CiProjects.AddAsync(sampleProject1);

        var sampleBuild1 = new CiBuild
        {
            CiProject = sampleProject1,
        };

        await db.CiBuilds.AddAsync(sampleBuild1);
        await db.SaveChangesAsync();

        var sampleJob1 = new CiJob
        {
            CiProjectId = sampleProject1.Id,
            CiBuildId = sampleBuild1.CiBuildId,
            CiJobId = 12,
        };
        await db.CiJobs.AddAsync(sampleJob1);
        await db.SaveChangesAsync();

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });

        listenerMockSetup.QueueCloseMessage();

        Assert.Null(sampleJob1.ReservedByRunner);

        // This is expected to return false, but we do want to check the server didn't reply incorrectly
        await listenerMockSetup.Start(false);

        await listenerMockSetup.WaitUntilClosed();

        Assert.True(listenerMockSetup.TryDequeueServerMessage(out var serverMessage, out _));
        Assert.NotNull(serverMessage);

        // We skipped the auto-auth, so we see this
        Assert.Equal(BuildSectionMessageType.AuthDemand, serverMessage.Type);

        // And then we expect the server to have closed
        Assert.False(listenerMockSetup.TryDequeueServerMessage(out serverMessage, out _));
        Assert.Null(serverMessage);

        await listenerMockSetup.CheckServerClosedSocket();
    }

    [Fact]
    public async Task Runner_CannotAccessJobsWithoutAuthWithWrongKey()
    {
        var listenerMockSetup =
            await RunnerConnectionMockHelper.Create(nameof(Runner_CannotAccessJobsWithoutAuthWithWrongKey), logger);

        var db = listenerMockSetup.AccessDatabase();

        var sampleProject1 = new CiProject
        {
            Name = "Sample project",
            Enabled = true,
        };
        await db.CiProjects.AddAsync(sampleProject1);

        var sampleBuild1 = new CiBuild
        {
            CiProject = sampleProject1,
        };

        await db.CiBuilds.AddAsync(sampleBuild1);
        await db.SaveChangesAsync();

        var sampleJob1 = new CiJob
        {
            CiProjectId = sampleProject1.Id,
            CiBuildId = sampleBuild1.CiBuildId,
            CiJobId = 12,
        };
        await db.CiJobs.AddAsync(sampleJob1);
        await db.SaveChangesAsync();

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage
            { Type = BuildSectionMessageType.AuthResponse, Output = "a banana" });

        listenerMockSetup.QueueMessage(new RealTimeBuildMessage { Type = BuildSectionMessageType.GetAvailableJobs });

        listenerMockSetup.QueueCloseMessage();

        Assert.Null(sampleJob1.ReservedByRunner);

        // This is expected to return false, but we do want to check the server didn't reply incorrectly
        await listenerMockSetup.Start(false);

        await listenerMockSetup.WaitUntilClosed();

        Assert.True(listenerMockSetup.TryDequeueServerMessage(out var serverMessage, out _));
        Assert.NotNull(serverMessage);

        // We skipped the auto-auth, so we see this
        Assert.Equal(BuildSectionMessageType.AuthDemand, serverMessage.Type);

        Assert.True(listenerMockSetup.TryDequeueServerMessage(out serverMessage, out _));
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.Error, serverMessage.Type);
        Assert.Contains("Invalid secret", serverMessage.ErrorMessage);

        Assert.False(listenerMockSetup.TryDequeueServerMessage(out serverMessage, out _));

        // And then we expect the server to have closed
        await listenerMockSetup.CheckServerClosedSocket();
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
