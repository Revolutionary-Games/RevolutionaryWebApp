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
            ReportedSize = (long)(GlobalConstants.MEBIBYTE * 1.12f),
            AutoIncrementEachTime = (long)(GlobalConstants.MEBIBYTE * 0.3f),
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
            new DummyJobExecutor.ExampleSection("Example2", "And second JOB_ID", true),
        ], [sampleJob1.GetDTO(), sampleJob2.GetDTO(), sampleJob3.GetDTO()]);

        var runnerData = new RunnerClientDataServiceObjet
        {
            ConnectionKey = listenerMockSetup.GetRunnerData().AccessId.ToString(),
            SecretKey = listenerMockSetup.GetRunnerData().SecretKey.ToString(),
            ServerUrl = "dummy.unittest.example.com",
            MaxCacheSize = (long)(GlobalConstants.MEBIBYTE * 1.2f),
            PruneCacheAfterSizeFraction = 1,
            KeepCacheSize = 0,
        };

        using var service =
            new RunnerService(logger, communicationMock, runnerData, executor, mockCache);

        var serviceShutdown = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var bridge = new RunnerClientAndServerMockBridge(listenerMockSetup, communicationMock);

        // To safely shut down the test, ask the runner to stop once it no longer gets jobs
        service.SetNoClientIdleMessageWait();
        service.StopWhenIdle();

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

        CheckWebMessagesForJob(listenerMockSetup, sampleJob1, true,
            (1, "Example output", ["This is the output\n"], true),
            (2, "Example2", ["And second 12"], true));

        CheckWebMessagesForJob(listenerMockSetup, sampleJob2, true,
            (1, "Example output", ["This is the output\n"], true),
            (2, "Example2", ["And second 13"], true));

        CheckWebMessagesForJob(listenerMockSetup, sampleJob3, true,
            (1, "Example output", ["This is the output\n"], true),
            (2, "Example2", ["And second 14"], true));

        Assert.False(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out var webMessage, out var group));
        Assert.Null(webMessage);
        Assert.Null(group);

        // And finally verify what got into the database
        // Need to get new DB here to confirm the data
        var second =
            await RunnerConnectionMockHelper.CreateResume(nameof(Runner_ClientAndServerCanTalkToEachOther), logger);

        var db2 = second.AccessDatabase();

        // We fetch last database data before closing the listener as it will dispose the DB instance we gave it
        // Check that the output actually reached the database
        await CheckDatabaseContents(db2, sampleJob1, true,
            (1, "Example output", ["This is the output\n"], true),
            (2, "Example2", ["And second 12"], true));

        await CheckDatabaseContents(db2, sampleJob2, true,
            (1, "Example output", ["This is the output\n"], true),
            (2, "Example2", ["And second 13"], true));

        await CheckDatabaseContents(db2, sampleJob3, true,
            (1, "Example output", ["This is the output\n"], true),
            (2, "Example2", ["And second 14"], true));
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private void CheckWebMessagesForJob(RunnerConnectionMockHelper listenerMockSetup, CiJob job,
        bool buildStatus, params (long Id, string SectionName, string[] Parts, bool Success)[] sections)
    {
        var expectedGroupName = $"{NotificationGroups.CIProjectsBuildsJobRealtimeOutputPrefix}{job.CiProjectId}_"
            + $"{job.CiBuildId}_{job.CiJobId}";

        foreach (var (id, name, parts, success) in sections)
        {
            Assert.True(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out var webMessage, out var group));
            Assert.NotNull(webMessage);
            Assert.Equal(expectedGroupName, group);
            Assert.Equal(BuildSectionMessageType.SectionStart, webMessage.Message.Type);
            Assert.Equal(id, webMessage.Message.SectionId);
            Assert.Equal(name, webMessage.Message.SectionName);

            foreach (var part in parts)
            {
                Assert.True(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out webMessage, out group));
                Assert.NotNull(webMessage);
                Assert.Equal(expectedGroupName, group);
                Assert.Equal(BuildSectionMessageType.BuildOutput, webMessage.Message.Type);
                Assert.Equal(id, webMessage.Message.SectionId);
                Assert.Equal(name, webMessage.Message.SectionName);
                Assert.Equal(part, webMessage.Message.Output);
            }

            Assert.True(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out webMessage, out group));
            Assert.NotNull(webMessage);
            Assert.Equal(expectedGroupName, group);
            Assert.Equal(BuildSectionMessageType.SectionEnd, webMessage.Message.Type);
            Assert.Equal(id, webMessage.Message.SectionId);
            Assert.Equal(name, webMessage.Message.SectionName);
            Assert.Equal(success, webMessage.Message.WasSuccessful);
        }

        Assert.True(listenerMockSetup.TryDequeueWebsiteNoticeMessage(out var webMessage2, out var group2));
        Assert.NotNull(webMessage2);
        Assert.Equal(expectedGroupName, group2);
        Assert.Equal(BuildSectionMessageType.FinalStatus, webMessage2.Message.Type);
        Assert.Equal(0, webMessage2.Message.SectionId);
        Assert.Null(webMessage2.Message.SectionName);
        Assert.Equal(buildStatus, webMessage2.Message.WasSuccessful);
    }

    private async Task CheckDatabaseContents(NotificationsEnabledDb db2, CiJob job,
        bool buildStatus, params (long Id, string SectionName, string[] Parts, bool Success)[] wantedSections)
    {
        // Check job state
        var data = await db2.CiJobs.FindAsync(job.CiProjectId, job.CiBuildId, job.CiJobId);
        Assert.NotNull(data);
        Assert.Equal(job.CiProjectId, data.CiProjectId);
        Assert.Equal(buildStatus, data.Succeeded);
        Assert.Equal(CIJobState.Finished, data.State);
        Assert.Null(data.ReservedByRunnerId);
        Assert.NotNull(data.FinishedAt);
        Assert.NotNull(data.RanOnServer);
        Assert.NotNull(data.TimeWaitingForServer);
        Assert.False(data.OutputPurged);

        // Then check sections
        var sections = await db2.CiJobOutputSections.Where(s =>
            s.CiProjectId == job.CiProjectId && s.CiBuildId == job.CiBuildId &&
            s.CiJobId == job.CiJobId).OrderBy(s => s.CiJobOutputSectionId).ToListAsync();

        Assert.Equal(wantedSections.Length, sections.Count);

        for (int i = 0; i < wantedSections.Length; ++i)
        {
            Assert.Equal(wantedSections[i].Id, sections[i].CiJobOutputSectionId);
            Assert.Equal(wantedSections[i].SectionName, sections[i].Name);
            Assert.Equal(string.Join(string.Empty, wantedSections[i].Parts), sections[i].Output);
            Assert.Equal(wantedSections[i].Success, sections[i].Status == CIJobSectionStatus.Succeeded);
            Assert.True(sections[i].Status != CIJobSectionStatus.Running);
            Assert.NotNull(sections[i].FinishedAt);
        }
    }
}
