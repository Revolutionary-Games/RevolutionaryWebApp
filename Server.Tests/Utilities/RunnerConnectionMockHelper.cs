namespace RevolutionaryWebApp.Server.Tests.Utilities;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Common.Utilities;
using Fixtures;
using Hangfire;
using Hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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
using Shared.Notifications;
using StackExchange.Redis;
using Xunit;
using FileAccess = DevCenterCommunication.Models.Enums.FileAccess;
using FileType = DevCenterCommunication.Models.Enums.FileType;

/// <summary>
///   Handles the common setup for mocking the dependencies needed by <see cref="RunnerConnectionHandler"/> and opening
///   a connection
/// </summary>
public class RunnerConnectionMockHelper
{
    private static int nextRunnerId;

    private readonly NotificationsEnabledDb database;
    private readonly RemoteRunner remoteRunner;
    private readonly string? databaseName;

    private readonly ConcurrentQueue<(RealTimeBuildMessage? Message, bool Closed)> messageQueue = new();

    private readonly ConcurrentQueue<(RealTimeBuildMessage? Message, bool Closed)> serverOutgoingQueue = new();

    private readonly ConcurrentQueue<(string Group, BuildMessageNotification Message)> websiteNoticeMessages = new();

    private readonly Guid runnerId = Guid.NewGuid();
    private readonly Guid secretKey = Guid.NewGuid();

    private readonly IBuildMessageSocketFactory socketFactory;
    private readonly IRealTimeBuildMessageSocket socket;
    private readonly IHubContext<NotificationsHub, INotifications> notifications;

    // Redis mock components to simulate pub/sub
    private readonly IConnectionMultiplexer redis;
    private readonly ISubscriber subscriber;
    private readonly Dictionary<string, Action<RedisChannel, RedisValue>> redisHandlers = new();

    private IServiceScopeFactory? scopeFactory;

    private RunnerConnectionHandler? connection;

    private bool closed;
    private bool serverClosed;

    protected RunnerConnectionMockHelper(NotificationsEnabledDb database, string? databaseName,
        RemoteRunner? existingRunner = null)
    {
        this.database = database;

        // Capture the in-memory database name so another context can be created after this one is disposed
        this.databaseName = databaseName;

        if (existingRunner != null)
        {
            remoteRunner = existingRunner;
            runnerId = existingRunner.AccessId;
            secretKey = existingRunner.SecretKey;
        }
        else
        {
            remoteRunner = new RemoteRunner($"Test Runner {GetNextId()}")
            {
                AccessId = runnerId,
                SecretKey = secretKey,
                HashedAccessId = SelectByHashedProperty.HashForDatabaseValue(runnerId.ToString()),
            };
        }

        socketFactory = Substitute.For<IBuildMessageSocketFactory>();
        socket = Substitute.For<IRealTimeBuildMessageSocket>();
        notifications = Substitute.For<IHubContext<NotificationsHub, INotifications>>();

        // Prepare redis and capture subscriptions so tests can trigger them
        redis = Substitute.For<IConnectionMultiplexer>();
        subscriber = Substitute.For<ISubscriber>();

        redis.GetSubscriber().Returns(subscriber);

        subscriber.SubscribeAsync(Arg.Any<RedisChannel>(), Arg.Any<Action<RedisChannel, RedisValue>>())
            .Returns(ci =>
            {
                var channel = ci.Arg<RedisChannel>().ToString();
                var handler = ci.Arg<Action<RedisChannel, RedisValue>>();

                // Store/replace the handler for the channel
                redisHandlers[channel] = handler;
                return Task.CompletedTask;
            });

        subscriber.UnsubscribeAllAsync().Returns(_ =>
        {
            redisHandlers.Clear();
            return Task.CompletedTask;
        });

        socket.Read(Arg.Any<CancellationToken>()).Returns(async call =>
        {
            // We need to emulate socket cancellation for all tests to be able to pass
            var cancellation = call.ArgAt<CancellationToken>(0);
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

                await Task.Delay(5, cancellation);
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

        var hubClient = Substitute.For<IHubClients<INotifications>>();

        notifications.Clients.Returns(hubClient);

        hubClient.Group(Arg.Any<string>()).Returns(data =>
        {
            var groupName = data.Arg<string>();
            var group = Substitute.For<INotifications>();

            group.ReceiveNotificationJSON(Arg.Any<string>()).Returns(messageData =>
            {
                // But if a waste to JSON decode it here, but otherwise all tests would have to do that
                var jsonData = messageData.Arg<string>();
                var notification = JsonSerializer.Deserialize<SerializedNotification>(jsonData,
                    new JsonSerializerOptions { Converters = { NotificationExtensions.Converter } });

                if (notification?.NotificationType != nameof(BuildMessageNotification))
                    throw new Exception("Sent a notice that wasn't a build message");

                var converted = (BuildMessageNotification)notification;

                if (converted.Message == null!)
                    throw new Exception("Sent a notice that couldn't be decoded correctly");

                websiteNoticeMessages.Enqueue((groupName, converted));

                if (websiteNoticeMessages.Count > 1000)
                    throw new InvalidOperationException("Too many web client notice messages are unread");

                return Task.CompletedTask;
            });

            return group;
        });
    }

    /// <summary>
    ///   Set to true to test SQL optimizations (only works with a real SQL database, not the default memory one)
    /// </summary>
    public bool UsesRealDatabase { get; set; }

    /// <summary>
    ///   Mock for remote downloads used by RunnerConnectionHandler to generate signed URLs for CI image download
    /// </summary>
    public IGeneralRemoteDownloadUrls? RemoteDownloadUrlsMock { get; private set; }

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

        var mockHelper = new RunnerConnectionMockHelper(existingDatabase, testName);

        existingDatabase.RemoteRunners.Add(mockHelper.remoteRunner);
        await existingDatabase.SaveChangesAsync();

        mockHelper.scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        var jobMock = Substitute.For<IBackgroundJobClient>();

        // Provide a simple remote download URL generator mock returning a constant string
        var remoteDownloadsMock = Substitute.For<IGeneralRemoteDownloadUrls>();
        remoteDownloadsMock.CreateDownloadFor(Arg.Any<StorageFile>(), Arg.Any<TimeSpan>())
            .Returns(_ => "https://example.invalid/dummy-ci-image-url");

        mockHelper.scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(serviceProvider);

        serviceProvider.GetService(typeof(NotificationsEnabledDb)).Returns(existingDatabase);
        serviceProvider.GetService(typeof(ILogger<RunnerConnectionHandler>)).Returns(logger);
        serviceProvider.GetService(typeof(IModelUpdateNotificationSender)).Returns(existingNotifications);
        serviceProvider.GetService(typeof(IHubContext<NotificationsHub, INotifications>))
            .Returns(mockHelper.notifications);
        serviceProvider.GetService(typeof(IBackgroundJobClient)).Returns(jobMock);
        serviceProvider.GetService(typeof(IGeneralRemoteDownloadUrls)).Returns(remoteDownloadsMock);

        mockHelper.RemoteDownloadUrlsMock = remoteDownloadsMock;

        return mockHelper;
    }

    public static async Task<RunnerConnectionMockHelper> CreateResume(string testName,
        ILogger<RunnerConnectionHandler> logger, IModelUpdateNotificationSender? existingNotifications = null,
        NotificationsEnabledDb? existingDatabase = null)
    {
        existingNotifications ??= Substitute.For<IModelUpdateNotificationSender>();

        if (existingDatabase == null)
        {
            var database = new EditableInMemoryDatabaseFixtureWithNotifications(existingNotifications, testName);

            existingDatabase = database.NotificationsEnabledDatabase;
        }

        var mockHelper = new RunnerConnectionMockHelper(existingDatabase, testName,
            await existingDatabase.RemoteRunners.FirstOrDefaultAsync() ?? throw new Exception("No existing runner"));

        mockHelper.scopeFactory = Substitute.For<IServiceScopeFactory>();
        var scope = Substitute.For<IServiceScope>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        var jobMock = Substitute.For<IBackgroundJobClient>();

        // Provide a simple remote download URL generator mock returning a constant string
        var remoteDownloadsMock = Substitute.For<IGeneralRemoteDownloadUrls>();
        remoteDownloadsMock.CreateDownloadFor(Arg.Any<StorageFile>(), Arg.Any<TimeSpan>())
            .Returns(_ => "https://example.invalid/dummy-ci-image-url");

        mockHelper.scopeFactory.CreateScope().Returns(scope);
        scope.ServiceProvider.Returns(serviceProvider);

        serviceProvider.GetService(typeof(NotificationsEnabledDb)).Returns(existingDatabase);
        serviceProvider.GetService(typeof(ILogger<RunnerConnectionHandler>)).Returns(logger);
        serviceProvider.GetService(typeof(IModelUpdateNotificationSender)).Returns(existingNotifications);
        serviceProvider.GetService(typeof(IHubContext<NotificationsHub, INotifications>))
            .Returns(mockHelper.notifications);
        serviceProvider.GetService(typeof(IBackgroundJobClient)).Returns(jobMock);
        serviceProvider.GetService(typeof(IGeneralRemoteDownloadUrls)).Returns(remoteDownloadsMock);

        mockHelper.RemoteDownloadUrlsMock = remoteDownloadsMock;

        return mockHelper;
    }

    /// <summary>
    ///   Creates a minimal CI image file hierarchy and uploaded version so that CI image lookup succeeds in tests.
    ///   The created file is marked as Special and write-locked (WriteAccess = Nobody) to simulate a previously used
    ///   CI image.
    /// </summary>
    /// <param name="database">The database to insert items into.</param>
    /// <param name="imageName">The CI image string like "thing/image:v1".</param>
    /// <returns>The created image item</returns>
    public static async Task<StorageItem> CreateBasicCIImageAsync(ApplicationDbContext database, string imageName)
    {
        // Build target path: CI/Images/<imageFileName>
        var fileName = new CiJob { Image = imageName }.GetImageFileName();

        // Ensure CI and Images folders exist
        var ciFolder = await database.StorageItems.FirstOrDefaultAsync(i => i.ParentId == null && i.Name == "CI");

        if (ciFolder == null)
        {
            ciFolder = new StorageItem
            {
                Name = "CI",
                Ftype = FileType.Folder,
                AllowParentless = true,
                ReadAccess = FileAccess.Developer,
                WriteAccess = FileAccess.Developer,
            };
            await database.StorageItems.AddAsync(ciFolder);
        }

        var imagesFolder =
            await database.StorageItems.FirstOrDefaultAsync(i => i.ParentId == ciFolder.Id && i.Name == "Images");

        if (imagesFolder == null)
        {
            imagesFolder = new StorageItem
            {
                Name = "Images",
                Ftype = FileType.Folder,
                Parent = ciFolder,
                ReadAccess = FileAccess.Developer,
                WriteAccess = FileAccess.Developer,
            };
            await database.StorageItems.AddAsync(imagesFolder);
        }

        // Create the file item marked as special and write-locked
        var imageItem =
            await database.StorageItems.FirstOrDefaultAsync(i => i.ParentId == imagesFolder.Id && i.Name == fileName);

        if (imageItem == null)
        {
            imageItem = new StorageItem
            {
                Name = fileName,
                Ftype = FileType.File,
                Parent = imagesFolder,
                Special = true,
                WriteAccess = FileAccess.Nobody,
                ReadAccess = FileAccess.Developer,
            };
            await database.StorageItems.AddAsync(imageItem);
        }

        // Ensure there's an uploaded version with a storage file
        var lowestVersion = await imageItem.GetLowestUploadedVersion(database);
        if (lowestVersion == null)
        {
            var version = await imageItem.CreateNextVersion(database, null);

            // Create a storage file for the version and mark uploading finished
            var storageFile = await version.CreateStorageFile(database, DateTime.UtcNow.AddMinutes(-10), 123);
            storageFile.OnVersionUploadFinished(version);

            imageItem.Size = 123;
            await database.StorageItemVersions.AddAsync(version);
        }

        await database.SaveChangesAsync();
        return imageItem;
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
            await RunnerConnectionHandler.HandleHttpConnection(customContext, scopeFactory, redis, socketFactory,
                UsesRealDatabase);

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

    public async Task CheckOpenNoticeWasSent()
    {
        var payload = JsonSerializer.Serialize(new
        {
            ConnectionId = connection?.GetConnectionId() ?? -1234,
            RunnerId = remoteRunner.Id,
        });

        await subscriber.Received().PublishAsync(
            Arg.Is<RedisChannel>(c => c.ToString() == NotificationGroups.RealtimeNewConnectionOpened),
            Arg.Is<RedisValue>(v => v.ToString() == payload));
    }

    public async Task CheckServerClosedSocket()
    {
        await socket.Received().Close(Arg.Any<CancellationToken>());
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

    public bool TryDequeueServerMessage(out RealTimeBuildMessage? message, out bool closedState,
        bool ignoreHeartbeat = true)
    {
        if (serverOutgoingQueue.TryDequeue(out var dequeued))
        {
            // Recursively call until we get a non-heartbeat message if we are ignoring those
            if (ignoreHeartbeat && dequeued.Message?.Type == BuildSectionMessageType.HeartBeat)
            {
                return TryDequeueServerMessage(out message, out closedState, ignoreHeartbeat);
            }

            closedState = dequeued.Closed;
            message = dequeued.Message;
            return true;
        }

        closedState = true;
        message = null;
        return false;
    }

    public async Task<RealTimeBuildMessage?> WaitForServerMessage(TimeSpan timeout = default)
    {
        if (timeout == TimeSpan.Zero)
            timeout = TimeSpan.FromSeconds(20);

        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            if (TryDequeueServerMessage(out var message, out _))
                return message;

            if (serverClosed)
                return null;

            await Task.Delay(1);
        }

        throw new Exception("Timed out waiting for a message from the server");
    }

    public async Task WaitUntilClosed(TimeSpan timeout = default)
    {
        if (connection == null)
            return;

        await connection.WaitUntilClosed(timeout);
    }

    /// <summary>
    ///   Simulates the Redis realtime notice that a new connection for the same runner has opened.
    /// </summary>
    public void TriggerRedisNewConnectionOpened(long newRunnerId, int newConnectionId)
    {
        // This payload must match RunnerConnectionHandler.NewConnectionOpenedNotice structure
        var payload = JsonSerializer.Serialize(new
        {
            ConnectionId = newConnectionId,
            RunnerId = newRunnerId,
        });

        if (redisHandlers.TryGetValue(NotificationGroups.RealtimeNewConnectionOpened, out var handler))
        {
            handler(new RedisChannel(NotificationGroups.RealtimeNewConnectionOpened, RedisChannel.PatternMode.Literal),
                new RedisValue(payload));
            return;
        }

        throw new InvalidOperationException(
            "No subscriber captured for RealtimeNewConnectionOpened; did you Start() the helper?");
    }

    /// <summary>
    ///   Creates a fresh NotificationsEnabledDb pointing to the same in-memory database store by name.
    ///   Useful for reading data after the handler has disposed of the scoped context.
    /// </summary>
    public NotificationsEnabledDb CreateNewDbContextForReading()
    {
        if (string.IsNullOrWhiteSpace(databaseName))
            throw new InvalidOperationException("Original database name is unknown; cannot re-open context");

        // Use a new notifications sender; EF InMemory identifies the store solely by its name
        var sender = Substitute.For<IModelUpdateNotificationSender>();
        var fixture = new EditableInMemoryDatabaseFixtureWithNotifications(sender, databaseName);
        return fixture.NotificationsEnabledDatabase;
    }

    public void DequeueAuthRequest(bool alsoSuccessResponse = true)
    {
        Assert.True(TryDequeueServerMessage(out var serverMessage, out _));
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.AuthDemand, serverMessage.Type);

        if (alsoSuccessResponse)
        {
            Assert.True(TryDequeueServerMessage(out serverMessage, out _));
            Assert.NotNull(serverMessage);
            Assert.Equal(BuildSectionMessageType.AuthSuccess, serverMessage.Type);
        }
    }

    /// <summary>
    ///   Waits for the server to send an auth request
    /// </summary>
    public async Task WaitDequeueAuthRequest(bool alsoSuccessResponse = true)
    {
        var serverMessage = await WaitForServerMessage();
        Assert.NotNull(serverMessage);
        Assert.Equal(BuildSectionMessageType.AuthDemand, serverMessage.Type);

        if (alsoSuccessResponse)
        {
            serverMessage = await WaitForServerMessage();
            Assert.NotNull(serverMessage);
            Assert.Equal(BuildSectionMessageType.AuthSuccess, serverMessage.Type);
        }
    }

    public bool TryDequeueWebsiteNoticeMessage([NotNullWhen(true)] out BuildMessageNotification? message,
        [NotNullWhen(true)] out string? groupName)
    {
        if (!websiteNoticeMessages.TryDequeue(out var data))
        {
            groupName = null;
            message = null;
            return false;
        }

        message = data.Message;
        groupName = data.Group;
        return true;
    }

    /// <summary>
    ///   Waits until the server has at least read each message, but note that the processing might still be occurring
    /// </summary>
    public async Task WaitUntilQueueEmpty()
    {
        var timeout = TimeSpan.FromSeconds(15);
        var start = DateTime.UtcNow;

        while (DateTime.UtcNow - start < timeout)
        {
            if (messageQueue.IsEmpty)
                return;

            await Task.Delay(5);
        }

        throw new TimeoutException("Message queue did not become empty within timeout");
    }

    public bool IsConnectionOpen()
    {
        return !closed;
    }

    public RemoteRunner GetRunnerData()
    {
        return remoteRunner;
    }
}
