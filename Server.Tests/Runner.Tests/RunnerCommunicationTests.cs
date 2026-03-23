namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System;
using System.Threading.Tasks;
using Server.Controllers;
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

        // Start things up
        await listenerMockSetup.Start();

        // Verify that the socket was accepted and some messages were exchanged
        await listenerMockSetup.CheckAuthHappened();
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
