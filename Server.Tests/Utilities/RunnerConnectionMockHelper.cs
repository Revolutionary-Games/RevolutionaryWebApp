namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
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
using Xunit;

/// <summary>
///   Handles the common setup for mocking the dependencies needed by <see cref="RunnerConnectionHandler"/> and opening
///   a connection
/// </summary>
public class RunnerConnectionMockHelper
{
    private static int nextRunnerId;

    private readonly NotificationsEnabledDb database;
    private readonly RemoteRunner remoteRunner;

    private readonly ConcurrentQueue<(RealTimeBuildMessage? Message, bool Closed)> messageQueue = new();

    private readonly ConcurrentQueue<(RealTimeBuildMessage? Message, bool Closed)> serverOutgoingQueue = new();

    private readonly Guid runnerId = Guid.NewGuid();
    private readonly Guid secretKey = Guid.NewGuid();

    private readonly IBuildMessageSocketFactory socketFactory;
    private readonly IRealTimeBuildMessageSocket socket;

    private IServiceScopeFactory? scopeFactory;

    private RunnerConnectionHandler? connection;

    private bool closed;
    private bool serverClosed;

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

        socket.Read(Arg.Any<CancellationToken>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            if (closed)
            {
                // This happens if the test puts messages in the queue in the wrong order, so this is a test bug
                if (messageQueue.Count > 0)
                    throw new InvalidOperationException("Socket is closed but there are still messages in the queue");

                return (null, true);
            }

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

        socket.Write(Arg.Any<RealTimeBuildMessage>(), Arg.Any<CancellationToken>()).Returns(data =>
        {
            if (serverClosed)
                throw new InvalidOperationException("Socket is closed");

            serverOutgoingQueue.Enqueue((data.Arg<RealTimeBuildMessage>(), false));

            if (serverOutgoingQueue.Count > 1000)
            {
                throw new InvalidOperationException(
                    "Server is sending too many messages to the runner without them being read");
            }

            return Task.CompletedTask;
        });

        socket.Close(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (!serverClosed)
            {
                serverClosed = true;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        });

        socket.CloseStatus.Returns(_ => serverClosed ? WebSocketCloseStatus.NormalClosure : null);
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

    public async Task<bool> Start(bool autoAuth = true, HttpContext? customContext = null)
    {
        if (scopeFactory == null)
            throw new InvalidOperationException("Not setup correctly");

        customContext ??= new DefaultHttpContext
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

            // If we have any messages already, we need to put the auth first
            var messages = messageQueue.ToArray();
            messageQueue.Clear();

            QueueMessage(new RealTimeBuildMessage
                { Type = BuildSectionMessageType.AuthResponse, Output = secretKey.ToString() });

            foreach (var message in messages)
            {
                QueueMessage(message.Message, message.Closed);
            }
        }

        connection =
            await RunnerConnectionHandler.HandleHttpConnection(customContext, scopeFactory, redis, socketFactory);

        return connection != null;
    }

    public async Task CheckAuthHappened()
    {
        await socketFactory.Received().AcceptAsync();
        await socket.Received().Write(
            Arg.Is<RealTimeBuildMessage>(m => m != null && m.Type == BuildSectionMessageType.AuthDemand),
            Arg.Any<CancellationToken>());

        Assert.True(connection != null);
    }

    public async Task CheckSocketWasNotAccepted()
    {
        await socketFactory.DidNotReceive().AcceptAsync();
    }

    /// <summary>
    ///   This queue can be used to send messages from the "runner" to the server
    /// </summary>
    public void QueueMessage(RealTimeBuildMessage? message, bool close = false)
    {
        if (closed)
            throw new InvalidOperationException("Socket is closed");

        messageQueue.Enqueue((message, close));

        if (messageQueue.Count > 1000)
            throw new InvalidOperationException("Too many messages in the queue");
    }

    public void QueueCloseMessage()
    {
        QueueMessage(null, true);
    }

    public NotificationsEnabledDb AccessDatabase()
    {
        return database;
    }

    public int GetQueuedMessageCount()
    {
        return messageQueue.Count;
    }

    public bool TryDequeueServerMessage(out RealTimeBuildMessage? message, out bool closedState)
    {
        if (serverOutgoingQueue.TryDequeue(out var dequeued))
        {
            closedState = dequeued.Closed;
            message = dequeued.Message;
            return true;
        }

        closedState = true;
        message = null;
        return false;
    }

    public async Task WaitUntilClosed(TimeSpan timeout = default)
    {
        if (connection == null)
            return;

        await connection.WaitUntilClosed(timeout);
    }

    public void DequeueAuthRequest()
    {
        Assert.True(TryDequeueServerMessage(out var serverMessage, out _));
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.AuthDemand, serverMessage.Type);
    }
}
