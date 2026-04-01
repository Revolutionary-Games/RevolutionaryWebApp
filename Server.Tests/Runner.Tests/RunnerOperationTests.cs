namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Models;
using Common.Services;
using Server.Models;
using Shared.Models;
using Shared.Models.Enums;
using TestUtilities.Utilities;
using Utilities;
using Xunit;
using Xunit.Abstractions;

/// <summary>
///   This is the client / runner side test equivalent of <see cref="RunnerCommunicationTests"/> (which tests the
///   server)
/// </summary>
public sealed class RunnerOperationTests(ITestOutputHelper output) : IDisposable
{
    private readonly XunitLogger<RunnerService> logger = new(output);

    [Fact]
    public async Task Runner_OnStartupAsksForJobs()
    {
        var communicationMock = new RunnerToClientMockHelper();

        using var service = new RunnerService(logger, communicationMock, communicationMock.GetDataForClient(),
            new DummyJobExecutor(false, [], []));

        var serviceShutdown = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var runTask = service.Run(serviceShutdown.Token);

        await communicationMock.PerformConnectionAuth();
        Assert.True(communicationMock.IsAuthenticated);
        Assert.True(communicationMock.IsConnected);

        // Wait for the first message from the client asking for a list of jobs
        var message = await communicationMock.WaitForClientMessage();
        Assert.NotNull(message);
        Assert.Equal(BuildSectionMessageType.GetAvailableJobs, message.Type);

        // And reply with an empty list
        communicationMock.SendJobsToClient();

        // Stop the service so we can test the shutdown
        service.StopAfterNextJob();

        // Make sure the runner didn't crash
        Assert.Equal(0, await runTask);

        Assert.False(communicationMock.IsConnected);
    }

    [Fact]
    public async Task Runner_StartsAndCanFinishAJob()
    {
        var communicationMock = new RunnerToClientMockHelper();
        var cacheSettings = JsonSerializer.Serialize(new CiJobCacheConfiguration());

        // Setup jobs to show
        var project1 = new CiProject
        {
            Id = 1,
            Name = "Test Project",
        };
        var build1 = new CiBuild
        {
            CiProject = project1,
            CiProjectId = 1,
            CiBuildId = 2,
        };

        var job1 = new CiJob
        {
            CiProjectId = project1.Id,
            Build = build1,
            CiBuildId = build1.CiBuildId,
            CiJobId = 15,
            State = CIJobState.Starting,
            CacheSettingsJson = cacheSettings,
        };

        communicationMock.AddJob(job1);

        // Set up and start the service
        var executor = new DummyJobExecutor(true,
        [
            new DummyJobExecutor.ExampleSection("Example output", "This is the output\n", true),
            new DummyJobExecutor.ExampleSection("Example2", "And second", false),
        ], [job1.GetDTO()]);

        using var service =
            new RunnerService(logger, communicationMock, communicationMock.GetDataForClient(), executor);

        var serviceShutdown = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var runTask = service.Run(serviceShutdown.Token);

        await communicationMock.PerformConnectionAuth();
        Assert.True(communicationMock.IsAuthenticated);

        var message = await communicationMock.WaitForClientMessage();
        Assert.NotNull(message);
        Assert.Equal(BuildSectionMessageType.GetAvailableJobs, message.Type);

        communicationMock.SendJobsToClient();

        await communicationMock.WaitForClientToStartJob(job1);
        Assert.Equal(CIJobState.Running, job1.State);

        // Stop the service so we can test the shutdown (it should shut down after the job is finished)
        service.StopAfterNextJob();

        await communicationMock.HandleMessagesUntilJobFinished();
        Assert.Equal(CIJobState.Finished, job1.State);

        Assert.Equal(0, await runTask);
        Assert.False(communicationMock.IsConnected);

        var done = executor.GetRunJobs().ToList();
        Assert.Single(done);

        Assert.True(communicationMock.GetJobFinishedStatus(job1));

        // Check that the output was sent correctly
        var output = communicationMock.GetJobOutputSections(job1);
        Assert.NotNull(output);

        Assert.Equal(2, output.Count);
        var output1 = output[0];
        var output2 = output[1];

        Assert.Equal(1, output1.Id);
        Assert.True(output1.Success);
        Assert.Equal("This is the output\n", output1.Text.ToString());
        Assert.Equal("Example output", output1.Name);

        Assert.Equal(2, output2.Id);
        Assert.False(output2.Success);
        Assert.Equal("And second", output2.Text.ToString());
        Assert.Equal("Example2", output2.Name);
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
