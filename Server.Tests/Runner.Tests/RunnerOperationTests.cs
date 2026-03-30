namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Common.Services;
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

        using var service = new RunnerService(logger, communicationMock, communicationMock.GetDataForClient());

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

        // Interrupt the service so we can test the shutdown
        service.StopAfterNextJob();

        await runTask;

        Assert.False(communicationMock.IsConnected);
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
