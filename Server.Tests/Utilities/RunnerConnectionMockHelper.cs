namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System;
using System.Collections.Concurrent;
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

/// <summary>
///   Handles the common setup for mocking the dependencies needed by <see cref="RunnerConnectionHandler"/> and opening
///   a connection
/// </summary>
public class RunnerConnectionMockHelper
{
    private static int nextRunnerId = 0;

    private readonly NotificationsEnabledDb database;
    private readonly RemoteRunner remoteRunner;

    private readonly ConcurrentQueue<(RealTimeBuildMessage? Message, bool Closed)> messageQueue = new();

    private readonly Guid runnerId = Guid.NewGuid();
    private readonly Guid secretKey = Guid.NewGuid();

    private readonly IBuildMessageSocketFactory socketFactory;
    private readonly IRealTimeBuildMessageSocket socket;

    private IServiceScopeFactory? scopeFactory;

    private bool closed;

    protected RunnerConnectionMockHelper(NotificationsEnabledDb database)
    {
        this.database = database;

        remoteRunner = new RemoteRunner($"Test Runner {GetNextId()}")
        {
            AccessId = runnerId,
            SecretKey = secretKey,
            HashedAccessId = SelectByHashedProperty.HashForDatabaseValue(runnerId.ToString()),
        };

        socketFactory = Substitute.For<IBuildMessageSocketFactory>();
        socket = Substitute.For<IRealTimeBuildMessageSocket>();

        socket.Read(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            if (closed)
                return (null, true);

            // Make sure this doesn't wait forever, which would just hang the unit tests but rather fail with an
            // exception after a long time
            for (int i = 0; i < 10000; ++i)
            {
                if (messageQueue.TryDequeue(out var message))
                {
                    // Permanently close once we see a close message
                    if (message.Closed)
                        closed = true;

                    return message;
                }

                await Task.Delay(5);
            }

            throw new Exception("Timed out waiting for a message for RunnerConnection");
        });
    }

    public static int GetNextId()
    {
        return Interlocked.Increment(ref nextRunnerId);
    }

    public static async Task<RunnerConnectionMockHelper> Create(string testName,
        ILogger<RunnerConnectionHandler> logger,
        IModelUpdateNotificationSender? existingNotifications = null, NotificationsEnabledDb? existingDatabase = null)
    {
        existingNotifications ??= Substitute.For<IModelUpdateNotificationSender>();

        if (existingDatabase == null)
        {
            var database = new EditableInMemoryDatabaseFixtureWithNotifications(existingNotifications, testName);

            existingDatabase = database.NotificationsEnabledDatabase;
        }

        var mockHelper = new RunnerConnectionMockHelper(existingDatabase);

        existingDatabase.RemoteRunners.Add(mockHelper.remoteRunner);
        await existingDatabase.SaveChangesAsync();

        mockHelper.scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        mockHelper.scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(serviceProvider);

        serviceProvider.GetService(typeof(NotificationsEnabledDb)).Returns(existingDatabase);
        serviceProvider.GetService(typeof(ILogger<RunnerConnectionHandler>)).Returns(logger);

        return mockHelper;
    }

    public async Task Start(bool autoAuth = true)
    {
        if (scopeFactory == null)
            throw new InvalidOperationException("Not setup correctly");

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

        var redis = Substitute.For<IConnectionMultiplexer>();

        socketFactory.AcceptAsync().Returns(socket);

        if (autoAuth)
        {
            // Assume the server correctly asks for auth so we can just directly enqueue the auth response
            QueueMessage(new RealTimeBuildMessage
                { Type = BuildSectionMessageType.AuthResponse, Output = secretKey.ToString() });
        }

        await RunnerConnectionHandler.HandleHttpConnection(dummyContext, scopeFactory, redis, socketFactory);
    }

    public async Task CheckAuthHappened()
    {
        await socketFactory.Received().AcceptAsync();
        await socket.Received().Write(
            Arg.Is<RealTimeBuildMessage>(m => m != null && m.Type == BuildSectionMessageType.AuthDemand),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    ///   This queue can be used to send messages from the "runner" to the server
    /// </summary>
    public void QueueMessage(RealTimeBuildMessage? message, bool close = false)
    {
        messageQueue.Enqueue((message, close));

        if (messageQueue.Count > 1000)
            throw new InvalidOperationException("Too many messages in the queue");
    }

    public void QueueCloseMessage()
    {
        QueueMessage(null, true);
    }
}
