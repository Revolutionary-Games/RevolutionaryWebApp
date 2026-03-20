namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Common.Utilities;
using DevCenterCommunication.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Models;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using StackExchange.Redis;
using Utilities;

/// <summary>
///   Handles realtime websocket connections from runner instances (they run CI jobs)
/// </summary>
public class RunnerConnectionHandler : IDisposable
{
    private readonly SemaphoreSlim cancelSetup = new(1, 1);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly RealTimeBuildMessageSocket socket;
    private readonly long runnerId;
    private readonly int connectionId;
    private readonly ISubscriber subscriber;
    private int prioritySeconds;

    // This dynamically creates database instances when needed as these socket connections can run for a really long
    // time
    private IServiceScope? currentScope;

    private ILogger? logger;
    private ApplicationDbContext? database;

    private CancellationTokenSource messageTimeout = new();
    private CancellationTokenSource newJobsNotice = new();

    private RunnerConnectionHandler(RealTimeBuildMessageSocket socket, IServiceScopeFactory scopeFactory, long runnerId,
        int connectionId, ISubscriber subscriber, int prioritySeconds)
    {
        this.scopeFactory = scopeFactory;
        this.socket = socket;
        this.runnerId = runnerId;
        this.connectionId = connectionId;
        this.subscriber = subscriber;
        UpdatePriority(prioritySeconds);
    }

    public static string GetNotificationGroup(CiJob job)
    {
        return NotificationGroups.CIProjectsBuildsJobRealtimeOutputPrefix + job.CiProjectId + "_" +
            job.CiBuildId + "_" + job.CiJobId;
    }

    public static async Task HandleHttpConnection(HttpContext context, IServiceScopeFactory scopeFactory,
        IConnectionMultiplexer realtimeCommunications)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        if (!context.Request.Query.TryGetValue("runnerId", out StringValues keyRaw) || keyRaw.Count != 1 ||
            !Guid.TryParse(keyRaw[0], out Guid key))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return;
        }

        using var setupScope = scopeFactory.CreateScope();
        var database = setupScope.ServiceProvider.GetRequiredService<NotificationsEnabledDb>();
        var logger = setupScope.ServiceProvider.GetRequiredService<ILogger<RunnerConnectionHandler>>();

        var runner = await database.RemoteRunners.WhereHashed(nameof(RemoteRunner.AccessId), key.ToString())
            .AsAsyncEnumerable().FirstOrDefaultAsync(r => r.AccessId == key);

        if (runner == null)
        {
            logger.LogWarning("Someone tried to connect to remote runner access with an invalid key");
            context.Response.ContentType = "plain/text";
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.Body.WriteAsync("Invalid key id or secret for runner"u8.ToArray());
            return;
        }

        using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();

        var neededSecret = runner.SecretKey.ToString();
        string providedSecret;

        // Client has 60 seconds to authenticate
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Open the socket and read the secret key
        // This should be safe as we should be behind an HTTPS proxy in production
        var wrappedSocket = new RealTimeBuildMessageSocket(webSocket);

        // We first request the key from the client
        await wrappedSocket.Write(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.AuthDemand,
        }, cancellation.Token);

        try
        {
            var (reply, closed) = await wrappedSocket.Read(cancellation.Token);

            if (closed)
            {
                logger.LogWarning("Client closed connection before auth was complete");
                return;
            }

            if (reply == null || reply.Type != BuildSectionMessageType.AuthResponse ||
                string.IsNullOrWhiteSpace(reply.Output) || reply.Output.Length > 500)
            {
                throw new Exception("Invalid auth response, wrong message type");
            }

            providedSecret = reply.Output;
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to read auth demand from client: {@E}", e);
            try
            {
                cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", cancellation.Token);
            }
            catch (Exception e2)
            {
                logger.LogWarning(e2, "Failed to close socket after auth failure");
            }

            return;
        }

        // Check the client has the right key
        if (!SecurityHelpers.SlowEquals(neededSecret, providedSecret))
        {
            logger.LogWarning("Client who knew runner id {Id} tried to authenticate with an incorrect secret",
                runner.AccessId);
            try
            {
                await wrappedSocket.Write(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.Error,
                    ErrorMessage = "Invalid secret",
                }, cancellation.Token);

                // Delay a bit for the client to be able to read the response
                await Task.Delay(TimeSpan.FromSeconds(1), cancellation.Token);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to send auth failure message to client");
            }

            try
            {
                // And then close the socket
                cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", cancellation.Token);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to close socket after auth failure");
            }
        }

        // Client is authenticated; they are the runner they say they are
        int connectionId = new Random().Next();

        runner.CurrentConnectionId = connectionId;

        var subscriber = realtimeCommunications.GetSubscriber();

        logger.LogInformation(
            "Accepted runner connection from {RemoteIpAddress} ({Connection})) for runner {Id} ({Name})",
            context.Connection.RemoteIpAddress, connectionId, runner.Id, runner.Name);

        var handler = new RunnerConnectionHandler(wrappedSocket, scopeFactory, runner.Id, connectionId, subscriber,
            runner.Priority);

        handler.StartRun();
    }

    public void StartRun()
    {
        // Run in the background as these are potentially very long-lived connections
        Task.Run(async () =>
        {
            try
            {
                await Run();
            }
            catch (Exception e)
            {
                Console.WriteLine("Leaked exception in runner connection handler: " + e);
            }

            try
            {
                Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to dispose runner connection handler: " + e);
            }
        });
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            ReleaseCurrentScope();
            cancelSetup.Dispose();
            messageTimeout.Dispose();
            newJobsNotice.Dispose();
        }
    }

    private async Task SetupNextReadTimeout()
    {
        await cancelSetup.WaitAsync();
        try
        {
            // Client must send a heartbeat every minute, so we give a little bit of leeway before failing
            var timed = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            // Reset this if we got info on this
            if (newJobsNotice.IsCancellationRequested)
                newJobsNotice = new CancellationTokenSource();

            messageTimeout = CancellationTokenSource.CreateLinkedTokenSource(timed.Token, newJobsNotice.Token);
        }
        finally
        {
            cancelSetup.Release();
        }
    }

    private async Task Run()
    {
        int emptyMessages = 0;
        var lastCheckConnection = DateTime.UtcNow;

        // Start listening to realtime redis notifications
        await subscriber.SubscribeAsync(new RedisChannel(NotificationGroups.RealtimeNewJobCreatedNotification,
                RedisChannel.PatternMode.Literal),
            (channel, value) =>
            {
                cancelSetup.Wait();
                try
                {
                    if (!newJobsNotice.IsCancellationRequested)
                        newJobsNotice.Cancel();
                }
                finally
                {
                    cancelSetup.Release();
                }
            });

        try
        {
            while (true)
            {
                // This sets up a deadline for the next message, but also for sending a new job notice
                await SetupNextReadTimeout();

                // Check that we are still allowed to be open every now and then
                if (DateTime.UtcNow - lastCheckConnection > TimeSpan.FromMinutes(5))
                {
                    // Release scope to clear out any outdated info before reloading
                    ReleaseCurrentScope();

                    var model = await GetRunnerModelData(false, messageTimeout.Token);
                    if (model.CurrentConnectionId != connectionId)
                    {
                        logger?.LogWarning(
                            "Runner connection {Id} has been closed by the server due to another connection for the runner starting",
                            connectionId);
                        break;
                    }

                    // Make sure our priority is up to date
                    UpdatePriority(model.Priority);

                    lastCheckConnection = DateTime.UtcNow;
                }

                try
                {
                    bool notifyNewJobsAfterProcessing = false;

                    RealTimeBuildMessage? message;
                    try
                    {
                        // The second cancellation token is used here so that we don't stop partway through reading a
                        // message if we get a notice about a new job
                        (message, var closed) = await socket.Read(messageTimeout.Token,
                            new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token);

                        if (closed)
                            break;
                    }
                    catch (TaskCanceledException)
                    {
                        bool rethrow = true;
                        message = null;

                        await cancelSetup.WaitAsync();
                        try
                        {
                            // If we need to notify the client about new builds, then that was not a serious error
                            if (newJobsNotice.IsCancellationRequested)
                            {
                                notifyNewJobsAfterProcessing = true;
                                rethrow = false;
                            }
                        }
                        finally
                        {
                            cancelSetup.Release();
                        }

                        if (rethrow)
                            throw;
                    }

                    // TODO: message variant where we *offer* jobs to the client

                    if (message == null)
                    {
                        ++emptyMessages;

                        if (emptyMessages > 10)
                        {
                            await UpdateRunnerModelData(runner =>
                            {
                                runner.LastTriggeredError = "Client is sending too many empty messages";
                                runner.BumpUpdatedAt();
                            }, messageTimeout.Token);

                            logger?.LogWarning("Client is sending too many empty messages");
                            await socket.Write(new RealTimeBuildMessage
                            {
                                Type = BuildSectionMessageType.Error,
                                ErrorMessage = "Client is sending too many empty messages",
                            }, messageTimeout.Token);
                            break;
                        }
                    }
                    else
                    {
                        emptyMessages = 0;

                        await HandleClientMessage(message);
                    }

                    if (notifyNewJobsAfterProcessing)
                    {
                        // Wait a bit based on our priority before we send the message (and hopefully, in the meantime
                        // the client does not send us anything)
                        await Task.Delay(TimeSpan.FromSeconds(prioritySeconds), CancellationToken.None);

                        await ReplyToClient(new RealTimeBuildMessage
                        {
                            Type = BuildSectionMessageType.NewJobsAvailable,
                        }, new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);
                    }
                }
                catch (TaskCanceledException)
                {
                    // Timed out
                    break;
                }
            }

            if (!await socket.Close(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token))
                logger?.LogWarning("Failed to close socket");

            logger?.LogInformation("Runner connection {Id} closed", connectionId);
        }
        catch (Exception e)
        {
            try
            {
                AcquireScope();
                logger?.LogError(e, "Error in runner connection processing");

                await UpdateRunnerModelData(runner =>
                {
                    runner.LastTriggeredError = "Runner connection error: " + e.Message;
                    runner.BumpUpdatedAt();
                });
            }
            catch (Exception e2)
            {
                Console.WriteLine($"We got a second level error in reporting a runner connection error: {e2}");
            }
        }
        finally
        {
            try
            {
                await subscriber.UnsubscribeAllAsync();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to unsubscribe from redis notifications: {e}");
            }
        }
    }

    private async Task HandleClientMessage(RealTimeBuildMessage message)
    {
        var processingMaxTime = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        switch (message.Type)
        {
            // TODO: CI job asking and reserving

            // TODO: CI output handling
            case BuildSectionMessageType.SectionStart:
                break;
            case BuildSectionMessageType.BuildOutput:
                break;
            case BuildSectionMessageType.SectionEnd:
                break;
            case BuildSectionMessageType.FinalStatus:
                break;
            case BuildSectionMessageType.Error:
                break;

            case BuildSectionMessageType.HeartBeat:
                // We got a heartbeat, so update the data
                await UpdateRunnerModelData(runner =>
                {
                    runner.LastHeartbeat = DateTime.UtcNow;
                    runner.BumpUpdatedAt();
                }, processingMaxTime.Token);
                break;

            case BuildSectionMessageType.AuthDemand:
            case BuildSectionMessageType.AuthResponse:
            case BuildSectionMessageType.AuthSuccess:
            default:
                await ReplyToClient(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.Error,
                    ErrorMessage = "Invalid message type",
                }, processingMaxTime.Token);
                break;
        }
    }

    private async Task ReplyToClient(RealTimeBuildMessage message, CancellationToken cancellationToken)
    {
        await socket.Write(message, cancellationToken);
    }

    /// <summary>
    ///   Does a load of the runner model data from the DB and updates it with the given action.
    /// </summary>
    private async Task UpdateRunnerModelData(Action<RemoteRunner> action, CancellationToken cancellationToken = default)
    {
        var db = AccessDatabase();

        var runner = await GetRunnerModelData(true, cancellationToken);

        action(runner);

        await db.SaveChangesWithConflictResolvingAsync(conflicts =>
        {
            DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflicts, runner);
            action(runner);
        }, cancellationToken);
    }

    private async Task<RemoteRunner> GetRunnerModelData(bool verifyConnection,
        CancellationToken cancellationToken = default)
    {
        var db = AccessDatabase();
        var runner = await db.RemoteRunners.FindAsync([runnerId], cancellationToken) ??
            throw new Exception("Runner not found");

        if (runner.CurrentConnectionId != connectionId && verifyConnection)
        {
            throw new ConnectionOutdatedException();
        }

        return runner;
    }

    private void ReleaseCurrentScope()
    {
        database?.Dispose();
        database = null;
        logger = null;
        currentScope?.Dispose();
        currentScope = null;
    }

    private ApplicationDbContext AccessDatabase()
    {
        if (database != null)
            return database;

        AcquireScope();
        database = currentScope!.ServiceProvider.GetRequiredService<NotificationsEnabledDb>();
        return database;
    }

    private void AcquireScope()
    {
        if (currentScope == null)
        {
            currentScope = scopeFactory.CreateScope();
            logger = currentScope.ServiceProvider.GetRequiredService<ILogger<RunnerConnectionHandler>>();
        }
    }

    private void UpdatePriority(int priority)
    {
        prioritySeconds = Math.Clamp(priority, 0, 15);
    }

    /// <summary>
    ///   Thrown when the same runner establishes a new web socket connection and the outdated connection detects
    ///   it is now a duplicate and has to close
    /// </summary>
    private class ConnectionOutdatedException() : Exception("Connection is outdated");
}
