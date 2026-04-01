namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Models;
using Common.Services;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Models;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using SharedBase.Utilities;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

/// <summary>
///   This test tests both the runner service and the server listener to make sure they work together and manage to
///   complete a dummy job.
/// </summary>
public sealed class RunnerIntegrationTests(ITestOutputHelper output) : IDisposable
{
    private readonly XunitLogger<RunnerConnectionHandler> logger = new(output);

    [Fact]
    public async Task Runner_ClientAndServerCanTalkToEachOther()
    {
        var communicationMock = new RunnerToClientMockHelper();
        var mockCache = new MockExecutorCache
        {
            AutoIncrementEachTime = GlobalConstants.MEBIBYTE,
        };

        var testCacheSettings = JsonSerializer.Serialize(new CiJobCacheConfiguration());

        var listenerMockSetup =
            await RunnerConnectionMockHelper.Create(nameof(Runner_ClientAndServerCanTalkToEachOther), logger);

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
            CacheSettingsJson = testCacheSettings,
        };

        await db.CiJobs.AddAsync(sampleJob2);

        var sampleJob3 = new CiJob
        {
            CiProjectId = sampleProject1.Id,
            CiBuildId = sampleBuild1.CiBuildId,
            CiJobId = 14,
            CacheSettingsJson = testCacheSettings,
        };

        await db.CiJobs.AddAsync(sampleJob3);
        await db.SaveChangesAsync();

        var executor = new DummyJobExecutor(true,
        [
            new DummyJobExecutor.ExampleSection("Example output", "This is the output\n", true),
            new DummyJobExecutor.ExampleSection("Example2", "And second", true),
        ], [sampleJob1.GetDTO(), sampleJob2.GetDTO(), sampleJob3.GetDTO()]);

        var runnerData = new RunnerClientDataServiceObjet
        {
            ConnectionKey = listenerMockSetup.GetRunnerData().AccessId.ToString(),
            SecretKey = listenerMockSetup.GetRunnerData().SecretKey.ToString(),
            ServerUrl = "dummy.unittest.example.com",
            MaxCacheSize = (long)(GlobalConstants.MEBIBYTE * 1.2f),
        };

        using var service =
            new RunnerService(logger, communicationMock, runnerData, executor, mockCache);

        var serviceShutdown = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bridge = new RunnerClientAndServerMockBridge(listenerMockSetup, communicationMock);

        // TODO: a call to quit the runner service when idle?

        // Start the runner
        var runTask = service.Run(serviceShutdown.Token);

        // And then the communication
        await bridge.RunBridge();

        // Check that the client closed
        Assert.Equal(0, await runTask);

        Assert.False(communicationMock.IsConnected);
        Assert.Equal(1, mockCache.CleanedTimes);

        // Check for web /redis notices
        await listenerMockSetup.CheckOpenNoticeWasSent();
        var expectedGroupName = $"{NotificationGroups.CIProjectsBuildsJobRealtimeOutputPrefix}{sampleJob1.CiProjectId}_"
            + $"{sampleJob1.CiBuildId}_{sampleJob1.CiJobId}";

        Assert.True(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out var webMessage, out var group));
        Assert.NotNull(webMessage);
        Assert.Equal(expectedGroupName, group);
        Assert.Equal(BuildSectionMessageType.SectionStart, webMessage.Message.Type);
        Assert.Equal(1, webMessage.Message.SectionId);
        Assert.Equal("Example section", webMessage.Message.SectionName);

        Assert.True(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out webMessage, out group));
        Assert.NotNull(webMessage);
        Assert.Equal(expectedGroupName, group);
        Assert.Equal(BuildSectionMessageType.BuildOutput, webMessage.Message.Type);
        Assert.Equal(1, webMessage.Message.SectionId);
        Assert.Equal("Example section", webMessage.Message.SectionName);
        Assert.Equal("This is a test message that should go into a section", webMessage.Message.Output);

        Assert.True(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out webMessage, out group));
        Assert.NotNull(webMessage);
        Assert.Equal(expectedGroupName, group);
        Assert.Equal(BuildSectionMessageType.SectionEnd, webMessage.Message.Type);
        Assert.Equal(1, webMessage.Message.SectionId);
        Assert.Equal("Example section", webMessage.Message.SectionName);
        Assert.True(webMessage.Message.WasSuccessful);

        Assert.True(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out webMessage, out group));
        Assert.NotNull(webMessage);
        Assert.Equal(expectedGroupName, group);
        Assert.Equal(BuildSectionMessageType.FinalStatus, webMessage.Message.Type);
        Assert.Equal(0, webMessage.Message.SectionId);
        Assert.Null(webMessage.Message.SectionName);
        Assert.True(webMessage.Message.WasSuccessful);

        Assert.False(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out webMessage, out group));
        Assert.Null(webMessage);
        Assert.Null(group);

        // And finally verify what got into the database
        // Need to get new DB here to confirm the data
        var second =
            await RunnerConnectionMockHelper.CreateResume(nameof(Runner_ClientAndServerCanTalkToEachOther), logger);

        var db2 = second.AccessDatabase();

        // We fetch last database data before closing the listener as it will dispose the DB instance we gave it
        // Check that the output actually reached the database
        var sections = await db2.CiJobOutputSections.Where(s =>
            s.CiProjectId == sampleJob1.CiProjectId && s.CiBuildId == sampleJob1.CiBuildId &&
            s.CiJobId == sampleJob1.CiJobId).ToListAsync(CancellationToken.None);

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

    public void Dispose()
    {
        logger.Dispose();
    }
}
