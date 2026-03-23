namespace RevolutionaryWebApp.Server.Tests.Runner.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Common.Utilities;
using Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Server.Utilities;
using Shared.Models;
using Shared.Models.Enums;
using StackExchange.Redis;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class RunnerCommunicationTests(ITestOutputHelper output) : IDisposable
{
    private readonly XunitLogger<RunnerConnectionHandler> logger = new(output);

    [Fact]
    public async Task Runner_CanConnectWithWebsocket()
    {
        var runnerId = Guid.NewGuid();
        var secretKey = Guid.NewGuid();

        var dummyContext = new DefaultHttpContext
        {
            Request =
            {
                Query = new QueryCollection(new Dictionary<string, StringValues>
                {
                    { "runnerId", runnerId.ToString() },
                }),
            },
        };

        var notifications = Substitute.For<IModelUpdateNotificationSender>();

        var database =
            new EditableInMemoryDatabaseFixtureWithNotifications(notifications, nameof(Runner_CanConnectWithWebsocket));

        var db = database.NotificationsEnabledDatabase;
        var runner = new RemoteRunner("Test Runner")
        {
            AccessId = runnerId,
            SecretKey = secretKey,
            HashedAccessId = SelectByHashedProperty.HashForDatabaseValue(runnerId.ToString()),
        };
        db.RemoteRunners.Add(runner);
        await db.SaveChangesAsync();

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(serviceProvider);

        serviceProvider.GetService(typeof(NotificationsEnabledDb)).Returns(database.NotificationsEnabledDatabase);
        serviceProvider.GetService(typeof(ILogger<RunnerConnectionHandler>)).Returns(logger);

        var redis = Substitute.For<IConnectionMultiplexer>();
        var socketFactory = Substitute.For<IBuildMessageSocketFactory>();
        var socket = Substitute.For<IRealTimeBuildMessageSocket>();

        socketFactory.AcceptAsync().Returns(socket);

        // This queue can be used to send messages from the "runner" to the server
        var messagesFromRunner = new Queue<(RealTimeBuildMessage?, bool)>();
        messagesFromRunner.Enqueue((new RealTimeBuildMessage
            { Type = BuildSectionMessageType.AuthResponse, Output = secretKey.ToString() }, false));

        // Let's also send a HeartBeat message to see it gets handled
        messagesFromRunner.Enqueue((new RealTimeBuildMessage { Type = BuildSectionMessageType.HeartBeat }, false));

        // And then close the connection
        messagesFromRunner.Enqueue((null, true));

        socket.Read(Arg.Any<CancellationToken>()).Returns(_ => messagesFromRunner.Dequeue());

        await RunnerConnectionHandler.HandleHttpConnection(dummyContext, scopeFactory, redis, socketFactory);

        // Verify that the socket was accepted and some messages were exchanged
        await socketFactory.Received().AcceptAsync();
        await socket.Received().Write(
            Arg.Is<RealTimeBuildMessage>(m => m != null && m.Type == BuildSectionMessageType.AuthDemand),
            Arg.Any<CancellationToken>());
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
