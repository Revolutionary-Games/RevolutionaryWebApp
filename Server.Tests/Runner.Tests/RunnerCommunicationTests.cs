namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
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

    public void Dispose()
    {
        logger.Dispose();
    }
}
