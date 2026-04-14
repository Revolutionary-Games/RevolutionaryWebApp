namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Common.Models;
using Common.Utilities;
using DevCenterCommunication.Models;
using Hangfire;
using Hubs;
using Jobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Models;
using Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using StackExchange.Redis;
using Utilities;
using FileAccess = DevCenterCommunication.Models.Enums.FileAccess;

/// <summary>
///   Handles realtime websocket connections from runner instances (they run CI jobs)
/// </summary>
public class RunnerConnectionHandler : IDisposable
{
    public const string ResumedSectionWarningText = "\nResumed output connection after losing connection to runner. " +
        "Buffered output from before this may be missing or out of order!\n";

    private const int ClientOptimalTextSize = 1000;

    private readonly SemaphoreSlim cancelSetup = new(1, 1);

    // Things are flushed when these intervals are reached *or* a new section is started
    private readonly TimeSpan clientSendInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan databaseSaveInternal = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory scopeFactory;
    private readonly IRealTimeBuildMessageSocket socket;
    private readonly long runnerId;
    private readonly int connectionId;
    private readonly ISubscriber subscriber;
    private readonly bool useEfficientOutputAppend;
    private int prioritySeconds;

    private bool connectionOutdated;

    private Task? mainRunTask;

    // This dynamically creates database instances when needed as these socket connections can run for a really long
    // time
    private IServiceScope? currentScope;

    private ILogger? logger;
    private ApplicationDbContext? database;
    private IHubContext<NotificationsHub, INotifications>? notifications;

    private CancellationTokenSource messageTimeout = new();
    private CancellationTokenSource redisNotice = new();

    /// <summary>
    ///   When working on a job, we do not want the DB connection to end.
    /// </summary>
    private bool isScopeRefreshBlocked;

    /// <summary>
    ///   Active job this runner is working on, if any.
    /// </summary>
    private CiJob? activeJob;

    private CiJobCacheConfigurationEnriched? activeJobCacheData;

    // Buffering info for the current output section, used to make things more efficient
    private StringBuilder pendingDatabaseTextForSection = new();
    private CiJobOutputSection? activeOutputSection;
    private DateTime lastSaveToDatabase = DateTime.MinValue;

    // Info for buffering to output to the website clients (we don't want to naively forward all messages in case they
    // are too big or too small)
    private StringBuilder pendingClientTextForSection = new();
    private CiJobOutputSection? activeClientOutputSection;
    private DateTime lastSendToClient = DateTime.MinValue;

    private DateTime lastHeartbeatToClient = DateTime.MinValue;

    private RunnerConnectionHandler(IRealTimeBuildMessageSocket socket, IServiceScopeFactory scopeFactory,
        long runnerId, int connectionId, ISubscriber subscriber, int prioritySeconds, bool useSql)
    {
        this.scopeFactory = scopeFactory;
        this.socket = socket;
        this.runnerId = runnerId;
        this.connectionId = connectionId;
        this.subscriber = subscriber;
        useEfficientOutputAppend = useSql;
        UpdatePriority(prioritySeconds);
    }

    public static string GetNotificationGroup(CiJob job)
    {
        return NotificationGroups.CIProjectsBuildsJobRealtimeOutputPrefix + job.CiProjectId + "_" +
            job.CiBuildId + "_" + job.CiJobId;
    }

    public static async Task NotifyOpenedConnection(long runnerId, int connectionId,
        IConnectionMultiplexer realtimeCommunications)
    {
        var subscriber = realtimeCommunications.GetSubscriber();

        await subscriber.PublishAsync(
            new RedisChannel(NotificationGroups.RealtimeNewConnectionOpened, RedisChannel.PatternMode.Literal),
            new RedisValue(JsonSerializer.Serialize(new NewConnectionOpenedNotice(connectionId, runnerId))));
    }

    public static async Task<RunnerConnectionHandler?> HandleHttpConnection(HttpContext context,
        IServiceScopeFactory scopeFactory, IConnectionMultiplexer realtimeCommunications,
        IBuildMessageSocketFactory socketFactory, bool databaseIsPostgres)
    {
        if (!context.Request.Query.TryGetValue("runnerId", out StringValues keyRaw) || keyRaw.Count != 1 ||
            !Guid.TryParse(keyRaw[0], out Guid key))
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            return null;
        }

        using var setupScope = scopeFactory.CreateScope();
        var database = setupScope.ServiceProvider.GetRequiredService<NotificationsEnabledDb>();
        var logger = setupScope.ServiceProvider.GetRequiredService<ILogger<RunnerConnectionHandler>>();

        var remoteAddress = context.Connection.RemoteIpAddress;

        var runner = await database.RemoteRunners.WhereHashed(nameof(RemoteRunner.AccessId), key.ToString())
            .AsAsyncEnumerable().FirstOrDefaultAsync(r => r.AccessId == key);

        if (runner == null)
        {
            logger.LogWarning("Someone tried to connect to remote runner access with an invalid key");
            context.Response.ContentType = "plain/text";
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.Body.WriteAsync("Invalid key id or secret for runner"u8.ToArray());
            return null;
        }

        var neededSecret = runner.SecretKey.ToString();
        string providedSecret;

        // Client has 60 seconds to authenticate
        var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        // Open the socket and read the secret key
        // This should be safe as we should be behind an HTTPS proxy in production
        var wrappedSocket = await socketFactory.AcceptAsync();

        try
        {
            // We first request the key from the client
            await wrappedSocket.Write(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.AuthDemand,
            }, cancellation.Token);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to send auth demand to client");
            try
            {
                cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await wrappedSocket.Close(cancellation.Token);
            }
            catch (Exception e2)
            {
                logger.LogWarning(e2, "Failed to close socket after auth failure");
            }

            return null;
        }

        try
        {
            var (reply, closed) = await wrappedSocket.Read(cancellation.Token);

            if (closed)
            {
                logger.LogWarning("Client closed connection before auth was complete");
                return null;
            }

            if (reply == null || reply.Type != BuildSectionMessageType.AuthResponse ||
                string.IsNullOrWhiteSpace(reply.Output) || reply.Output.Length > 500)
            {
                throw new Exception($"Invalid auth response, wrong message type: {reply?.Type}");
            }

            providedSecret = reply.Output;
        }
        catch (Exception e)
        {
            logger.LogWarning("Failed to read auth response from client: {@E}", e);
            try
            {
                cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await wrappedSocket.Close(cancellation.Token);
            }
            catch (Exception e2)
            {
                logger.LogWarning(e2, "Failed to close socket after auth failure");
            }

            return null;
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

                // await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Auth failed", cancellation.Token);
                await wrappedSocket.Close(cancellation.Token);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to close socket after auth failure");
            }

            // Very important to not allow proceeding
            return null;
        }

        // Client is authenticated; they are the runner they say they are
        int connectionId = new Random().Next();

        runner.CurrentConnectionId = connectionId;
        runner.LastIpAddress = remoteAddress;

        // Need to save the data here to make sure the runner state is saved properly
        try
        {
            await database.SaveChangesAsync(cancellation.Token);
        }
        catch (Exception e)
        {
            // Not getting to save this, fails the connection starting
            logger.LogError(e, "Failed to save runner connection id, was there a DB conflict just now?");

            try
            {
                cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await wrappedSocket.Close(cancellation.Token);
            }
            catch (Exception e2)
            {
                logger.LogWarning(e2, "Failed to close socket after database write failure");
            }

            return null;
        }

        // Send a success message
        var successMessageSend = wrappedSocket.Write(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.AuthSuccess,
        }, new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

        // Let other connections know about the new one and that they should exit if they hold relevant buffers
        try
        {
            await NotifyOpenedConnection(runner.Id, connectionId, realtimeCommunications);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to publish runner connection to redis");
        }

        logger.LogInformation(
            "Accepted runner connection from {RemoteIpAddress} ({Connection}) for runner {Id} ({Name})",
            remoteAddress, connectionId, runner.Id, runner.Name);

        try
        {
            await successMessageSend;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to send auth success message to client, opening connection still");
        }

        var handler = new RunnerConnectionHandler(wrappedSocket, scopeFactory, runner.Id, connectionId,
            realtimeCommunications.GetSubscriber(), runner.Priority, databaseIsPostgres);

        handler.StartRun();
        return handler;
    }

    public void StartRun()
    {
        if (mainRunTask != null)
            throw new InvalidOperationException("Runner connection handler already started");

        // Run in the background as these are potentially very long-lived connections
        mainRunTask = Task.Run(async () =>
        {
            try
            {
                await Run();
            }
            catch (Exception e)
            {
                Console.WriteLine("Leaked exception in runner connection handler: " + e);
            }

            logger?.LogInformation("Runner connection handler finished");

            try
            {
                isScopeRefreshBlocked = false;
                Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to dispose runner connection handler: " + e);
            }
        });
    }

    public async Task WaitUntilClosed(TimeSpan timeout)
    {
        if (timeout == TimeSpan.Zero)
        {
            timeout = TimeSpan.FromSeconds(15);
        }

        if (mainRunTask == null)
            return;

        await mainRunTask.WaitAsync(timeout);
        mainRunTask = null;
    }

    public async Task WaitUntilClosed(CancellationToken cancellation)
    {
        if (mainRunTask == null)
            return;

        await mainRunTask.WaitAsync(cancellation);
        mainRunTask = null;
    }

    public int GetConnectionId()
    {
        return connectionId;
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
            redisNotice.Dispose();

            // This might be called by the task, so probably not safe to dispose of it?
            mainRunTask = null;
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
            if (redisNotice.IsCancellationRequested)
                redisNotice = new CancellationTokenSource();

            messageTimeout = CancellationTokenSource.CreateLinkedTokenSource(timed.Token, redisNotice.Token);
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

        // Start listening to realtime redis notifications to know when there are new jobs or when we become outdated
        // and should stop doing anything
        await SetupRedisListeners();

        // If a previous connection handler lost a connection while a job was running, we need to resume the job and
        // open the last section if there is one for our runner
        await ResumeJobAndSectionIfExists(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);

        try
        {
            while (true)
            {
                // If we have already been outdated, don't even start running
                if (connectionOutdated)
                {
                    logger?.LogInformation("Cannot start runner listening loop as we are already outdated");
                    break;
                }

                // This sets up a deadline for the next message, but also for sending a new job notice
                await SetupNextReadTimeout();

                // Check that we are still allowed to be open every now and then
                if (DateTime.UtcNow - lastCheckConnection > TimeSpan.FromMinutes(5))
                {
                    if (!isScopeRefreshBlocked)
                    {
                        // Release scope to clear out any outdated info before reloading
                        ReleaseCurrentScope();
                    }

                    var model = await GetRunnerModelData(false, messageTimeout.Token);
                    if (model.CurrentConnectionId != connectionId)
                    {
                        logger?.LogWarning(
                            "Runner connection {Id} has been closed by the server due to another connection " +
                            "for the runner starting", connectionId);
                        connectionOutdated = true;
                        break;
                    }

                    // Make sure our priority is up to date
                    UpdatePriority(model.Priority);

                    lastCheckConnection = DateTime.UtcNow;
                }

                try
                {
                    bool notifyNewJobsAfterProcessing = false;

                    // Don't stop to wait for any more messages after connection outdated signal
                    if (connectionOutdated)
                        break;

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
                            if (redisNotice.IsCancellationRequested)
                            {
                                notifyNewJobsAfterProcessing = true;
                                rethrow = false;
                            }
                        }
                        finally
                        {
                            cancelSetup.Release();
                        }

                        // Flushing is outside the loop
                        if (connectionOutdated)
                            break;

                        if (rethrow)
                            throw;
                    }

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

                        // Sanity check a few max limits, and if violated, tell the client to fix itself
                        if (message.SectionName is { Length: > 100 } ||
                            message.Output is { Length: > 20000 } || message.ErrorMessage is { Length: > 10000 })
                        {
                            logger?.LogWarning("Client sent a message that is too long");

                            await ReplyToClient(new RealTimeBuildMessage
                            {
                                Type = BuildSectionMessageType.Error,
                                ErrorMessage = "Too long message",
                            }, messageTimeout.Token);

                            await UpdateRunnerModelData(runner =>
                            {
                                runner.LastTriggeredError = "Client sent a message that is too long";
                                runner.BumpUpdatedAt();
                            });

                            break;
                        }

                        await HandleClientMessage(message);
                    }

                    // Flush client and database output if time, or they are long already
                    if (activeOutputSection != null)
                    {
                        await FlushSectionDataIfTooLongOrTime(new CancellationTokenSource(TimeSpan.FromSeconds(60))
                            .Token);
                    }

                    if (notifyNewJobsAfterProcessing)
                    {
                        // Wait a bit based on our priority before we send the message (and hopefully, in the meantime
                        // the client does not send us anything)
                        await Task.Delay(TimeSpan.FromSeconds(prioritySeconds), CancellationToken.None);

                        await ReplyToClient(new RealTimeBuildMessage
                        {
                            Type = BuildSectionMessageType.NewJobsAvailable,
                        });
                    }
                }
                catch (TaskCanceledException)
                {
                    // Timed out
                    break;
                }
            }

            if (connectionOutdated)
            {
                try
                {
                    logger?.LogInformation(
                        "We got notice that we are an outdated connection, so we will flush and close");

                    await FlushPendingText(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token);

                    activeOutputSection = null;
                    activeClientOutputSection = null;
                }
                catch (Exception e)
                {
                    logger?.LogError(e, "Failed to flush pending text");
                }
            }

            if (!await socket.Close(new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token))
                logger?.LogWarning("Failed to close socket");

            logger?.LogInformation("Runner connection id {Id} closed", connectionId);
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

        // TODO: should we close the last section under certain circumstances? We wouldn't always want to do that as
        // if the client just lost a connection temporarily, the build might still be continuing and once reconnected
        // the client might want to resume.
    }

    private async Task SetupRedisListeners()
    {
        await subscriber.SubscribeAsync(new RedisChannel(NotificationGroups.RealtimeNewJobCreatedNotification,
                RedisChannel.PatternMode.Literal),
            (_, _) =>
            {
                cancelSetup.Wait();
                try
                {
                    if (!redisNotice.IsCancellationRequested)
                        redisNotice.Cancel();
                }
                finally
                {
                    cancelSetup.Release();
                }
            });

        // Listener for detecting other connection opening for the same runner, and we should need to flush
        await subscriber.SubscribeAsync(
            new RedisChannel(NotificationGroups.RealtimeNewConnectionOpened, RedisChannel.PatternMode.Literal),
            (_, value) =>
            {
                var data = value.ToString();

                if (!value.HasValue || string.IsNullOrWhiteSpace(data))
                {
                    return;
                }

                // If another runner connection opens with the same ID, we need to flush
                NewConnectionOpenedNotice parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<NewConnectionOpenedNotice>(data) ??
                        throw new Exception("Deserialized is null");
                }
                catch (Exception e)
                {
                    logger?.LogError(e, "We got invalid redis data for new connection opening: {Data}", data);
                    return;
                }

                if (parsed.RunnerId != runnerId)
                {
                    // Message for someone else
                    return;
                }

                if (parsed.ConnectionId == connectionId)
                    logger?.LogWarning("We somehow got a connection notice about ourself?");

                // This connection is now outdated as there's a newer one made, so we will want to flush and exit soon
                connectionOutdated = true;

                logger?.LogInformation(
                    "We got notice that {Id1} is the new connection for runner {Runner} so connection {Id2} " +
                    "will close soon", parsed.ConnectionId, parsed.RunnerId, connectionId);

                cancelSetup.Wait();
                try
                {
                    if (!redisNotice.IsCancellationRequested)
                        redisNotice.Cancel();
                }
                finally
                {
                    cancelSetup.Release();
                }
            });
    }

    private async Task HandleClientMessage(RealTimeBuildMessage message)
    {
        var processingMaxTime = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        try
        {
            Validator.ValidateObject(message, new ValidationContext(message), true);
        }
        catch (Exception e)
        {
            logger?.LogWarning(e, "Invalid message received from client");
            await ReplyToClient(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.Error,
                ErrorMessage = "Invalid message received",
            }, processingMaxTime.Token);
            return;
        }

        switch (message.Type)
        {
            case BuildSectionMessageType.SectionStart:
            {
                if (!await CheckAndErrorIfNoActiveJob(processingMaxTime.Token))
                    return;

                // Close the previous section if it was left open
                BufferTextOutputIfSectionOpen("Section not closed by runner!");
                await TryFinishCurrentSection(new RealTimeBuildMessage
                {
                    // TODO: should this be false?
                    WasSuccessful = true,
                    Type = BuildSectionMessageType.SectionEnd,
                }, processingMaxTime.Token, true);

                // And then open a new one
                await StartNewSection(message, processingMaxTime.Token);
                break;
            }

            case BuildSectionMessageType.BuildOutput:
            {
                if (!await CheckAndErrorIfNoActiveJob(processingMaxTime.Token))
                    return;

                // Bigger output is buffered into reasonable segments
                if (activeOutputSection == null)
                {
                    // We need to start a section
                    await StartNewSection(message, processingMaxTime.Token);
                    BufferTextOutputIfSectionOpen("Received output before section started!");

                    if (message.Output is { Length: > 0 })
                        BufferTextOutputIfSectionOpen(message.Output, false);
                }
                else if (activeOutputSection != null && (activeOutputSection.Name != message.SectionName ||
                             activeOutputSection.CiJobOutputSectionId != message.SectionId))
                {
                    logger?.LogWarning(
                        "We are getting build messages for a non-active section, swapping to a new section");

                    BufferTextOutputIfSectionOpen(
                        "Section not closed by runner before sending a message for a new section!");
                    await TryFinishCurrentSection(new RealTimeBuildMessage
                    {
                        WasSuccessful = true,
                        Type = BuildSectionMessageType.SectionEnd,
                    }, processingMaxTime.Token, false);

                    try
                    {
                        await StartNewSection(message, processingMaxTime.Token);
                    }
                    catch (Exception e)
                    {
                        logger?.LogError(e, "Could not swap to a section the runner sent a message for");
                        await ReplyToClient(new RealTimeBuildMessage
                        {
                            Type = BuildSectionMessageType.Error,
                            ErrorMessage = "Invalid section to send a message for",
                        }, processingMaxTime.Token);
                    }
                }
                else
                {
                    // Can normally just append text
                    if (message.Output is { Length: > 0 })
                    {
                        if (!BufferTextOutputIfSectionOpen(message.Output, false))
                        {
                            logger?.LogWarning("We mishandled an output message and didn't add it to a section");
                            await ReplyToClient(new RealTimeBuildMessage
                            {
                                Type = BuildSectionMessageType.Error,
                                ErrorMessage = "Internal server error for adding to section",
                            }, processingMaxTime.Token);
                        }

                        // We don't need to check for a flush here as that is automatically checked after processing
                        // each message
                    }
                }

                break;
            }

            case BuildSectionMessageType.SectionEnd:
            {
                if (!await CheckAndErrorIfNoActiveJob(processingMaxTime.Token))
                    return;

                if (!await TryFinishCurrentSection(message, processingMaxTime.Token, false))
                {
                    logger?.LogWarning("Failed to finish current section");
                    await ReplyToClient(new RealTimeBuildMessage
                    {
                        Type = BuildSectionMessageType.Error,
                        ErrorMessage = "Could not close the section",
                    }, processingMaxTime.Token);
                }

                break;
            }

            case BuildSectionMessageType.FinalStatus:
            {
                if (!await CheckAndErrorIfNoActiveJob(processingMaxTime.Token))
                    return;

                if (!await TryFinishJob(message, processingMaxTime.Token))
                {
                    await ReplyToClient(new RealTimeBuildMessage
                    {
                        Type = BuildSectionMessageType.Error,
                        ErrorMessage = "Could not finish working on the job",
                    }, processingMaxTime.Token);

                    logger?.LogWarning("Failed to finish job");
                    isScopeRefreshBlocked = false;
                }

                // Reply with something so that the client read can end quickly, and as for success we don't have a
                // special message, we just send a heartbeat here
                await ReplyToClient(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.HeartBeat,
                }, processingMaxTime.Token);

                break;
            }

            case BuildSectionMessageType.Error:
                // TODO: should we save these on the runner to show?
                logger?.LogWarning("Client sent an error message: {Message}", message.ErrorMessage);
                break;

            case BuildSectionMessageType.GetAvailableJobs:
            {
                if (activeJob != null)
                {
                    // Client cannot ask for more jobs to run if it has one already
                    await ReplyWithCurrentJobDetails(processingMaxTime.Token);
                    return;
                }

                int skip = 0;

                if (!string.IsNullOrEmpty(message.Output) && int.TryParse(message.Output, out var clientSkip))
                {
                    skip = Math.Clamp(clientSkip, 0, 100);
                }

                var db = AccessDatabase();

                var runner = await GetRunnerModelData(false, processingMaxTime.Token);

                if (runner.DisallowJobs)
                {
                    // Not allowed jobs, so don't give any
                    await ReplyToClient(new RealTimeBuildMessage
                    {
                        Type = BuildSectionMessageType.JobsList,
                        Output = JsonSerializer.Serialize(new AvailableJobsList
                        {
                            Jobs = new List<CIJobDTO>(),
                            FilteredCount = -2,
                        }),
                    }, processingMaxTime.Token);
                    break;
                }

                // We don't load these as tracking because we don't really care about this data, just to give it to the
                // client
                var validJobs = await db.CiJobs.AsNoTracking()
                    .Where(j => (j.State == CIJobState.Starting || j.State == CIJobState.WaitingForServer) &&
                        j.ReservedByRunnerId == null).OrderBy(j => j.CreatedAt).Skip(skip).Take(10)
                    .ToListAsync(processingMaxTime.Token);

                int removed = validJobs.RemoveAll(j => FilterOutJobTags(j, runner));

                var dtoList = validJobs.ConvertAll(j => j.GetDTO());

                // We need to get project names for everything, so quickly fetch those as well
                {
                    var neededProjects = dtoList.Select(j => j.CiProjectId).Distinct().ToList();

                    var projects = await db.CiProjects.AsNoTracking().Where(p => neededProjects.Contains(p.Id))
                        .ToListAsync(processingMaxTime.Token);

                    foreach (var ciJobDTO in dtoList)
                    {
                        ciJobDTO.ProjectName = projects.First(p => p.Id == ciJobDTO.CiProjectId).Name;
                    }
                }

                await ReplyToClient(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.JobsList,
                    Output = JsonSerializer.Serialize(new AvailableJobsList
                    {
                        Jobs = dtoList,
                        FilteredCount = removed,
                    }),
                }, processingMaxTime.Token);

                break;
            }

            case BuildSectionMessageType.RequestStartJob:
            {
                long projectId = -1;
                long buildId = -1;
                long jobId = -1;

                bool validData = false;

                var data = message.Output?.Split(':', 3);

                if (data != null)
                {
                    if (long.TryParse(data[0], out projectId) && long.TryParse(data[1], out buildId) &&
                        long.TryParse(data[2], out jobId))
                    {
                        validData = true;
                    }
                    else
                    {
                        logger?.LogWarning("Invalid job id format in request start job message");
                    }
                }

                if (!validData)
                {
                    await ReplyToClient(new RealTimeBuildMessage
                    {
                        Type = BuildSectionMessageType.Error,
                        ErrorMessage = "Invalid message format",
                    }, processingMaxTime.Token);
                    break;
                }

                // If the runner already has a job, tell it to work on that instead!
                if (activeJob != null)
                {
                    await ReplyWithCurrentJobDetails(processingMaxTime.Token);
                    break;
                }

                if (!await TryToStartWorkingOnJob(projectId, buildId, jobId, processingMaxTime.Token))
                {
                    await ReplyToClient(new RealTimeBuildMessage
                    {
                        Type = BuildSectionMessageType.Error,
                        ErrorMessage = "Could not start working on the job (some other runner probably took it)",
                    }, processingMaxTime.Token);
                }

                break;
            }

            case BuildSectionMessageType.HeartBeat:
            {
                // Reply to the heartbeat if it's been more than 10 seconds since we did so
                if (DateTime.UtcNow - lastHeartbeatToClient > TimeSpan.FromSeconds(10))
                {
                    await ReplyToClient(new RealTimeBuildMessage
                    {
                        Type = BuildSectionMessageType.HeartBeat,
                    }, processingMaxTime.Token);
                    lastHeartbeatToClient = DateTime.UtcNow;
                }

                // We got a heartbeat, so update the data
                await UpdateRunnerModelData(runner =>
                {
                    runner.LastHeartbeat = DateTime.UtcNow;
                    runner.BumpUpdatedAt();
                }, processingMaxTime.Token);
                break;
            }

            case BuildSectionMessageType.AuthResponse:
                logger?.LogWarning("Runner sent an auth response but we weren't expecting one");
                await ReplyToClient(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.Error,
                    ErrorMessage = "Server was not expecting auth response at this point",
                }, processingMaxTime.Token);
                break;

            case BuildSectionMessageType.AuthDemand:
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

    private async Task ReplyWithCurrentJobDetails(CancellationToken cancellationToken)
    {
        if (activeJob == null)
            throw new Exception("No active job");

        if (activeJobCacheData == null)
            throw new InvalidOperationException("Active job cache data not set up yet");

        await ReplyToClient(new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.ActiveJobDetails,
            Output = JsonSerializer.Serialize(new RunningJobDetails(activeJob.GetDTO())
            {
                CacheConfiguration = activeJobCacheData,
            }),
        }, cancellationToken);
    }

    private bool FilterOutJobTags(CiJob obj, RemoteRunner runner)
    {
        if (obj.RequiredRunnerTags != null)
        {
            var tags = obj.RequiredRunnerTags.Split(';');

            bool matched = false;

            if (runner.Tags.Contains(';'))
            {
                var runnerTags = runner.Tags.Split(';');

                foreach (var tag in tags)
                {
                    if (runnerTags.Contains(tag))
                    {
                        matched = true;
                        break;
                    }
                }
            }
            else
            {
                foreach (var tag in tags)
                {
                    if (tag == runner.Tags)
                    {
                        matched = true;
                        break;
                    }
                }
            }

            if (!matched)
                return true;
        }

        // TODO: exclude tags in runner

        // Passed the filter
        return false;
    }

    private async Task<bool> CheckAndErrorIfNoActiveJob(CancellationToken cancellationToken)
    {
        if (activeJob == null)
        {
            logger?.LogWarning("Tried to do something with a job when there was no active job");

            await ReplyToClient(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.Error,
                ErrorMessage = "No active job (required for this message)",
            }, cancellationToken);

            return false;
        }

        return true;
    }

    private async Task<bool> TryToStartWorkingOnJob(long ciProject, long ciBuild, long ciJob,
        CancellationToken cancellationToken)
    {
        if (activeJob != null)
        {
            logger?.LogWarning("Runner tried to start working on a job when it already has one");
            return false;
        }

        try
        {
            var db = AccessDatabase();

            // Disable working on a job if already some job is reserved
            var reservedJob = await db.CiJobs.AnyAsync(j => j.ReservedByRunnerId == connectionId, cancellationToken);

            if (reservedJob)
            {
                logger?.LogWarning("Runner tried to start working on a job when it already has a job reserved");
                await ReplyToClient(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.Error,
                    ErrorMessage = "You already have a job reserved, please work on it or abandon it",
                }, cancellationToken);
                return false;
            }

            var job = await db.CiJobs.FirstOrDefaultAsync(j => j.CiProjectId == ciProject && j.CiBuildId == ciBuild &&
                j.CiJobId == ciJob && j.ReservedByRunnerId == null &&
                (j.State == CIJobState.Starting || j.State == CIJobState.WaitingForServer), cancellationToken);

            // The connection logic handles the case that the job is reserved by the current runner to resume the
            // job, so we don't need to handle it here.

            if (job == null)
                return false;

            var ourData = await GetRunnerModelData(false, cancellationToken);

            if (ourData.DisallowJobs)
            {
                logger?.LogWarning("Runner that is disallowed to start new jobs tried to start one");
                await ReplyToClient(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.Error,
                    ErrorMessage = "You are not allowed to start new jobs",
                }, cancellationToken);
                return false;
            }

            // If the job cannot be accepted due to tags, do not allow it!
            if (FilterOutJobTags(job, ourData))
            {
                logger?.LogWarning("Runner tried to start working on a job that is not allowed by the runner");
                await ReplyToClient(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.Error,
                    ErrorMessage = "Job not allowed for you due to runner tags",
                }, cancellationToken);
                return false;
            }

            // Parse cache config before accepting the job, in case there is a problem with it
            if (job.CacheSettingsJson == null)
            {
                logger?.LogWarning("Tried to start working on a job that does not have cache settings");
                throw new InvalidOperationException("Job is in invalid state (has no cache settings)");
            }

            var cacheConfig = JsonSerializer.Deserialize<CiJobCacheConfiguration>(job.CacheSettingsJson) ??
                throw new Exception("Parsed cache config is null");

            if (ourData.Id != runnerId)
                throw new Exception("Runner id mismatch after fetch");

            job.ReservedByRunner = ourData;
            job.ReservedByRunnerId = runnerId;
            job.OutputConnection = connectionId;

            // We got the job, but if someone writes to the DB first, we will fail
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException e)
            {
                // Clear the old job state which should allow us to keep using the DB instance
                // This can also happen for jobs that aren't the one we just modified
                // We pass true here to force the old job state to be unmodified, as we do not want to update it in any
                // way
                DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(e.Entries, job, true);

                if (job.ReservedByRunnerId == null)
                {
                    logger?.LogWarning(
                        "We made a whoopsie by resetting a reserved job to not have a runner ID so we might see it " +
                        "incorrectly again...");
                }

                // Somebody else got the job first
                logger?.LogDebug("Somebody got the job first that our runner would have wanted");
                return false;
            }

            logger?.LogInformation("We were able to reserve a new job for our runner: {Id}", runnerId);

            job.TimeWaitingForServer = DateTime.UtcNow - job.CreatedAt;

            job.State = CIJobState.Running;
            job.RanOnServer = ourData.Name;

            activeJobCacheData = await EnrichConfig(cacheConfig, job);
            activeJob = job;
            isScopeRefreshBlocked = true;

            ourData.TotalJobsTaken += 1;

            await db.SaveChangesAsync(cancellationToken);

            OnClearPreviousSectionData();

            // As this method is meant to be used when the runner did not have a job before, there should not be able
            // to be any previous section data

            // Let the client know what it got
            await ReplyWithCurrentJobDetails(cancellationToken);

            return true;
        }
        catch (Exception e)
        {
            logger?.LogInformation(e, "Couldn't reserve job for runner");
            activeJobCacheData = null;
            return false;
        }
    }

    private async Task<bool> TryFinishJob(RealTimeBuildMessage finalData, CancellationToken cancellationToken)
    {
        // Close the last section just in case it was left open
        BufferTextOutputIfSectionOpen("Section not closed by runner!");
        await TryFinishCurrentSection(new RealTimeBuildMessage
        {
            WasSuccessful = finalData.WasSuccessful,
            Type = BuildSectionMessageType.SectionEnd,
        }, cancellationToken, true);

        if (activeJob == null)
        {
            logger?.LogError("Tried to finish a job when there was no active job (processing got too far)");
            return false;
        }

        if (!string.IsNullOrEmpty(finalData.Output))
        {
            // Output is incorrect to send here as the client should have closed the section
            logger?.LogWarning("Final job completion message should not have output anymore");
        }

        var db = AccessDatabase();

        void ApplyJobState()
        {
            // Disallow us from overwriting any finished job status.
            // But we use conflict resolution to make sure that a finish is conserved in case another connection
            // still did some writing or something like that.
            if (activeJob.State == CIJobState.Finished)
                throw new Exception("Job already finished while we were doing conflict resolution");

            activeJob.State = CIJobState.Finished;

            // Very important to not clear cache settings on failure to allow re-running
            /*if (finalData.WasSuccessful)
            {
                logger?.LogInformation("Clearing job cache settings as it was successful");
                activeJob.CacheSettingsJson = null;
            }*/

            activeJob.FinishedAt = DateTime.UtcNow;
            activeJob.Succeeded = finalData.WasSuccessful;

            // We want the job to no longer be marked for us
            activeJob.ReservedByRunner = null;
            activeJob.ReservedByRunnerId = null;
        }

        ApplyJobState();

        // Set the final job status and save
        await db.SaveChangesWithConflictResolvingAsync(conflicts =>
        {
            DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflicts, activeJob);
            ApplyJobState();
        }, cancellationToken);

        logger?.LogInformation("Job {Project}-{Build}-{Job} finished by runner {Name}", activeJob.CiProjectId,
            activeJob.CiBuildId, activeJob.CiJobId, runnerId);

        // Clean up the message a bit in case it is dirty and then send it to all listening clients
        // And we must send this before unsetting the job as then we can no longer send jobs
        finalData.Output = null;
        finalData.ErrorMessage = null;
        await SendMessageToWebsiteClients(finalData);

        var job = activeJob;
        activeJob = null;

        isScopeRefreshBlocked = false;

        OnJobFinished(job);

        return true;
    }

    private void OnJobFinished(CiJob job)
    {
        // We must have the scope already here as we accessed the DB
        if (currentScope == null)
        {
            logger?.LogError("No current scope when job finished, cannot create task to check overall build status");
            return;
        }

        var jobClient = currentScope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();

        // Queue a background job to update the overall build status and trigger further actions
        jobClient.Enqueue<SetFinishedCIJobStatusJob>(x => x.Execute(job.CiProjectId, job.CiBuildId,
            job.CiJobId, job.Succeeded, CancellationToken.None));
    }

    private bool BufferTextOutputIfSectionOpen(string text, bool autoAddLineChange = true)
    {
        var added = false;

        if (activeOutputSection != null)
        {
            if (autoAddLineChange && pendingDatabaseTextForSection.Length > 0 &&
                pendingDatabaseTextForSection[^1] != '\n')
            {
                pendingDatabaseTextForSection.Append('\n');
            }

            pendingDatabaseTextForSection.Append(text);
            added = true;
        }

        if (activeClientOutputSection != null)
        {
            if (autoAddLineChange && pendingClientTextForSection.Length > 0 &&
                pendingClientTextForSection[^1] != '\n')
            {
                pendingClientTextForSection.Append('\n');
            }

            pendingClientTextForSection.Append(text);
            added = true;
        }

        return added;
    }

    private async Task FlushSectionDataIfTooLongOrTime(CancellationToken cancellationToken)
    {
        bool send = false;

        // Check cached length and time to determine if we should send info
        if (pendingClientTextForSection.Length > ClientOptimalTextSize * 2)
        {
            send = true;
        }

        if (pendingDatabaseTextForSection.Length > 100000)
        {
            send = true;
        }

        if (activeOutputSection != null)
        {
            var now = DateTime.UtcNow;

            var elapsedClient = now - lastSendToClient;
            var elapsedDatabase = now - lastSaveToDatabase;

            if (elapsedClient > clientSendInterval || elapsedDatabase > databaseSaveInternal)
            {
                send = true;
            }
        }

        if (send)
        {
            await FlushPendingText(cancellationToken);
        }
    }

    private async Task FlushPendingText(CancellationToken cancellationToken)
    {
        // Send client text
        if (pendingClientTextForSection.Length > 0)
        {
            if (activeClientOutputSection != null)
            {
                // Send text to clients in certain bunches
                for (int i = 0; i < pendingClientTextForSection.Length; i += ClientOptimalTextSize)
                {
                    var text = pendingClientTextForSection.ToString(i,
                        Math.Min(ClientOptimalTextSize, pendingClientTextForSection.Length - i));

                    await SendMessageToWebsiteClients(new RealTimeBuildMessage
                    {
                        Output = text,
                        Type = BuildSectionMessageType.BuildOutput,
                        SectionName = activeClientOutputSection.Name,
                        SectionId = activeClientOutputSection.CiJobOutputSectionId,
                    });

                    if (cancellationToken.IsCancellationRequested)
                        break;
                }
            }
            else
            {
                logger?.LogError("There is pending client text but no active output section for it");
            }

            pendingClientTextForSection.Clear();
            lastSendToClient = DateTime.UtcNow;
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        if (pendingDatabaseTextForSection.Length < 1)
            return;

        if (activeOutputSection == null)
        {
            logger?.LogError("There is pending database text but no active output section for it");
            return;
        }

        lastSaveToDatabase = DateTime.UtcNow;

        var db = AccessDatabase();

        if (useEfficientOutputAppend)
        {
            // We can use SQL to directly append to the database
            var text = pendingDatabaseTextForSection.ToString();
            var length = text.Length;

            // Apparently concat is slightly slower than just appending
            // SET output = concat(output, {text}), output_length = output_length + {length}
            FormattableString formattable =
                $"""
                     UPDATE ci_job_output_sections 
                     SET output = output || {text}, output_length = output_length + {length} 
                     WHERE ci_project_id = {activeOutputSection.CiProjectId} 
                       AND ci_build_id = {activeOutputSection.CiBuildId} AND ci_job_id = {activeOutputSection.CiJobId}
                 """;

            await db.Database.ExecuteSqlInterpolatedAsync(formattable, cancellationToken);
        }
        else
        {
            // TODO: should we have conflict resolution here?
            activeOutputSection.Output += pendingDatabaseTextForSection;
            activeOutputSection.OutputLength = activeOutputSection.Output.Length;

            await db.SaveChangesAsync(cancellationToken);
        }

        pendingDatabaseTextForSection.Clear();
    }

    private async Task ResumeJobAndSectionIfExists(CancellationToken cancellationToken)
    {
        if (activeJob != null)
            throw new InvalidOperationException("This may not be called once there is a job");

        var db = AccessDatabase();

        // See if there is an active job first as usually there shouldn't be because active jobs are only left if
        // the connection is lost unexpectedly
        var jobToResume = await db.CiJobs.AsNoTracking().Where(j => j.ReservedByRunnerId == runnerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobToResume == null)
            return;

        logger?.LogInformation("We found a job for our runner it should resume working on");

        // Wait one second to hopefully let anything else trying to work on the job to just stop safely
        await Task.Delay(1000, cancellationToken);

        // We now get the job as tracking to hopefully get the latest data
        // In theory we don't need to include the project, but it is done here as it doesn't really hurt and tests
        // work better (due to the exact same data verification)
        var job = await db.CiJobs.Include(j => j.Build).ThenInclude(b => b!.CiProject)
            .Where(j => j.ReservedByRunnerId == runnerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (job == null)
            throw new Exception("Job was not found after waiting to resume it");

        // Parse cache config before accepting the job, in case there is a problem with it
        if (job.CacheSettingsJson == null)
        {
            logger?.LogWarning("Tried to start working on a job that does not have cache settings");
            throw new InvalidOperationException("Job is in invalid state (has no cache settings)");
        }

        var cacheConfig = JsonSerializer.Deserialize<CiJobCacheConfiguration>(job.CacheSettingsJson) ??
            throw new Exception("Parsed cache config is null");

        // Update the connection ID that is now responsible for this job
        job.OutputConnection = connectionId;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException e)
        {
            throw new Exception("We couldn't correctly resume a job that a connection should be working on", e);
        }

        logger?.LogInformation("We were able to update the connection ID used by the job to: {Id}",
            job.OutputConnection);

        activeJobCacheData = await EnrichConfig(cacheConfig, job);
        activeJob = job;
        isScopeRefreshBlocked = true;

        // We will let the client know what it is working on the next time it tries to ask for a new job

        // Now that we have resumed the job, we need to also resume a section (if there is one)
        await ResumeSectionIfExists(cancellationToken);
    }

    /// <summary>
    ///   This loads extra data needed by the runner to actually run the job
    /// </summary>
    /// <returns>The input object (it's been modified in-place to be enriched)</returns>
    private async Task<CiJobCacheConfigurationEnriched?> EnrichConfig(CiJobCacheConfiguration cacheConfig, CiJob job)
    {
        if (currentScope == null)
            throw new InvalidOperationException("There must be a scope before trying to enrich a config");

        var source = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var cancellationToken = source.Token;

        var db = AccessDatabase();
        var fullJob = await db.CiJobs.Include(j => j.Build).ThenInclude(b => b!.CiProject).FirstOrDefaultAsync(j =>
                j.CiJobId == job.CiJobId && j.CiBuildId == job.CiBuildId && j.CiProjectId == job.CiProjectId,
            cancellationToken);

        if (fullJob == null)
            throw new Exception("We couldn't find the job with all navigations loaded from the database");

        if (fullJob.Build?.CiProject == null)
            throw new Exception("Job has no build or build has no project");

        var imageFileName = job.GetImageFileName();
        var serverSideImagePath = Path.Join("CI/Images", imageFileName);

        StorageItem? imageItem;
        try
        {
            imageItem = await StorageItem.FindByPath(db, serverSideImagePath);
        }
        catch (Exception e)
        {
            // ReSharper disable once ExceptionPassedAsTemplateArgumentProblem
            logger?.LogError("Invalid image specified for CI job: {Image}, path parse exception: {@E}", job.Image,
                e);
            job.SetFinishSuccess(false);
            await job.CreateFailureSection(db, "Invalid image specified for job (invalid path)");
            job.ReservedByRunner = null;
            job.ReservedByRunnerId = null;
            OnJobFinished(job);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (string.IsNullOrEmpty(job.Image) || imageItem == null)
        {
            logger?.LogError("Invalid image specified for CI job: {Image}", job.Image);
            job.SetFinishSuccess(false);
            await job.CreateFailureSection(db, "Invalid image specified for job (not found)");
            job.ReservedByRunner = null;
            job.ReservedByRunnerId = null;
            OnJobFinished(job);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        // The CI system uses the first valid image version. For future updates a different file name is needed.
        // For example, bumping the ":v1" to a ":v2" suffix
        var version = await imageItem.GetLowestUploadedVersion(db);

        if (version?.StorageFile == null)
        {
            logger?.LogError("Image with no uploaded version specified for CI job: {Image}", job.Image);
            job.SetFinishSuccess(false);
            await job.CreateFailureSection(db, "Invalid image specified for job (not uploaded version)");
            job.ReservedByRunner = null;
            job.ReservedByRunnerId = null;
            OnJobFinished(job);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }

        // Queue a job to lock writing to the CI image if it isn't write-protected yet
        if (imageItem.WriteAccess != FileAccess.Nobody)
        {
            logger?.LogInformation("Storage item {Id} used as CI image is not write locked, queuing a job to lock it",
                imageItem.Id);

            var jobClient = currentScope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();

            // To ensure the upload time is expired, this is upload time + 5 minutes
            jobClient.Schedule<LockCIImageItemJob>(x => x.Execute(imageItem.Id, CancellationToken.None),
                AppInfo.RemoteStorageUploadExpireTime + TimeSpan.FromMinutes(5));
        }

        var remoteDownloadUrls = currentScope.ServiceProvider.GetRequiredService<IGeneralRemoteDownloadUrls>();

        var imageDownloadUrl =
            remoteDownloadUrls.CreateDownloadFor(version.StorageFile, AppInfo.RemoteStorageDownloadExpireTime);

        var augmented = new CiJobCacheConfigurationEnriched
        {
            // Copy base properties
            LoadFrom = cacheConfig.LoadFrom,
            WriteTo = cacheConfig.WriteTo,
            Shared = cacheConfig.Shared,
            System = cacheConfig.System,

            // And then the special extra data
            CIImageFileName = imageFileName,
            CIImageName = job.Image,
            CIBranch = fullJob.Build.Branch ?? "unknown_branch",
            CIDefaultBranch = fullJob.Build.CiProject.DefaultBranch,
            CIJobName = job.JobName,
            CIRef = fullJob.Build.RemoteRef,
            IsSafe = fullJob.Build.IsSafe,
            CIEarlierCommit = fullJob.Build.PreviousCommit ?? AppInfo.NoCommitHash,
            CICommitHash = fullJob.Build.CommitHash,
            CIOrigin = fullJob.Build.CiProject.RepositoryCloneUrl,
            CIImageDownloadUrl = imageDownloadUrl,
        };

        CISecretType jobSpecificSecretType = fullJob.Build.IsSafe ? CISecretType.SafeOnly : CISecretType.UnsafeOnly;

        var secrets = await db.CiSecrets.Where(s => s.CiProjectId == job.CiProjectId &&
                (s.UsedForBuildTypes == jobSpecificSecretType || s.UsedForBuildTypes == CISecretType.All))
            .ToListAsync(cancellationToken);

        // Remove all type secrets if there is one with the same name that is build-specific
        var cleanedSecrets = secrets
            .Where(s => s.UsedForBuildTypes != CISecretType.All || !secrets.Any(s2 =>
                s2.SecretName == s.SecretName && s2.UsedForBuildTypes != s.UsedForBuildTypes))
            .Select(s => s.ToExecutorData());

        augmented.CISecrets = cleanedSecrets.ToList();

        return augmented;
    }

    private async Task ResumeSectionIfExists(CancellationToken cancellationToken)
    {
        if (activeJob == null)
            throw new InvalidOperationException("No active job");

        if (activeOutputSection != null)
            throw new InvalidOperationException("Cannot call this if there's an open section");

        var db = AccessDatabase();

        var notClosedSection = await db.CiJobOutputSections.Where(s =>
                s.CiProjectId == activeJob.CiProjectId && s.CiBuildId == activeJob.CiBuildId &&
                s.CiJobId == activeJob.CiJobId && (s.Status == CIJobSectionStatus.Running || s.FinishedAt == null))
            .ToListAsync(cancellationToken);

        if (notClosedSection.Count < 1)
            return;

        if (notClosedSection.Count > 1)
        {
            logger?.LogWarning(
                "There are multiple sections open for the same job, this is a problem, we'll resume the highest ID");
        }

        var toResume = notClosedSection.OrderByDescending(s => s.CiJobOutputSectionId).First();

        activeOutputSection = toResume;
        activeClientOutputSection = toResume;

        BufferTextOutputIfSectionOpen(ResumedSectionWarningText);

        // When resuming, we'll want to wait a bit before any data sending or saving again to the database
        lastSaveToDatabase = DateTime.UtcNow;
        lastSendToClient = DateTime.UtcNow;
    }

    private async Task StartNewSection(RealTimeBuildMessage message, CancellationToken cancellationToken)
    {
        if (activeOutputSection != null)
            throw new InvalidOperationException("Trying to start a new section with one open");

        if (activeJob == null)
            throw new InvalidOperationException("Trying to start a new section without a job");

        if (string.IsNullOrWhiteSpace(message.SectionName))
        {
            logger?.LogWarning("Tried to start a new section with an empty name");

            await ReplyToClient(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.Error,
                ErrorMessage = "Section name cannot be empty",
            }, cancellationToken);
            return;
        }

        // Small messages that shouldn't happen too often are directly forwarded to the clients on the website
        var websiteOpen = new RealTimeBuildMessage
        {
            SectionId = message.SectionId,
            Type = BuildSectionMessageType.SectionStart,
            SectionName = message.SectionName,
        };

        var db = AccessDatabase();

        var existingSection = await db.CiJobOutputSections.FirstOrDefaultAsync(s =>
            s.CiJobOutputSectionId == message.SectionId && s.CiProjectId == activeJob.CiProjectId &&
            s.CiBuildId == activeJob.CiBuildId && s.CiJobId == activeJob.CiJobId, cancellationToken);

        if (existingSection == null)
        {
            // Beginning a new section
            existingSection = new CiJobOutputSection
            {
                CiProjectId = activeJob.CiProjectId,
                CiBuildId = activeJob.CiBuildId,
                CiJobId = activeJob.CiJobId,
                CiJobOutputSectionId = message.SectionId,
                Name = message.SectionName,
                StartedAt = DateTime.UtcNow,
                Output = message.Output ?? string.Empty,
                OutputLength = message.Output?.Length ?? 0,
            };

            await db.CiJobOutputSections.AddAsync(existingSection, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            // Only notify clients if this is actually a new section
            await SendMessageToWebsiteClients(websiteOpen);
        }

        // Only buffer text for the client as we saved it in the database already
        activeClientOutputSection = existingSection;
        activeOutputSection = null;

        if (!string.IsNullOrWhiteSpace(message.Output))
        {
            BufferTextOutputIfSectionOpen(message.Output, false);
        }

        activeOutputSection = existingSection;

        if (!string.IsNullOrEmpty(message.ErrorMessage))
        {
            logger?.LogWarning("Start section message had an error: {Message}", message.ErrorMessage);
            BufferTextOutputIfSectionOpen("Section start error: " + message.ErrorMessage);
        }

        lastSaveToDatabase = DateTime.UtcNow;

        // Send new data to the client ASAP, but for the database we can wait a bit until saving anything
        lastSendToClient = DateTime.MinValue;
    }

    private async Task<bool> TryFinishCurrentSection(RealTimeBuildMessage finalData,
        CancellationToken cancellationToken, bool isSoftClose)
    {
        if (activeOutputSection == null)
        {
            if (!isSoftClose)
            {
                // We want to warn only when the client performed an action that we must close a section for.
                // In many cases for safety we will close a section if one exists but don't want to do anything if
                // nothing to do.
                logger?.LogWarning("Tried to finish a section when there was no active section");
            }

            return false;
        }

        if (finalData.SectionName != null && finalData.SectionName != activeOutputSection.Name)
        {
            logger?.LogWarning("Tried to finish a section with a different name than the one we started with");
        }

        // TODO: check if these mismatch in some cases?
        finalData.SectionId = activeOutputSection.CiJobOutputSectionId;
        finalData.SectionName = activeOutputSection.Name;

        if (!string.IsNullOrEmpty(finalData.Output))
            BufferTextOutputIfSectionOpen(finalData.Output, false);

        // Flush all pending text for the section before closing it
        await FlushPendingText(cancellationToken);

        var websiteClose = new RealTimeBuildMessage
        {
            SectionId = finalData.SectionId,
            Type = BuildSectionMessageType.SectionEnd,
            SectionName = finalData.SectionName,
            WasSuccessful = finalData.WasSuccessful,
        };
        var websiteSend = SendMessageToWebsiteClients(websiteClose);

        try
        {
            var db = AccessDatabase();

            // Update last section status. Output is already flushed and updated, so we don't need to worry about it
            activeOutputSection.FinishedAt = DateTime.UtcNow;
            activeOutputSection.Status =
                finalData.WasSuccessful ? CIJobSectionStatus.Succeeded : CIJobSectionStatus.Failed;

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            // TODO: do we need to log these better?
            logger?.LogError(e, "Failed to save section status");
        }

        activeOutputSection = null;
        activeClientOutputSection = null;

        await websiteSend;
        return true;
    }

    private async Task ReplyToClient(RealTimeBuildMessage message, CancellationToken cancellationToken = default)
    {
        if (cancellationToken == CancellationToken.None)
        {
            cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token;
        }

        await socket.Write(message, cancellationToken);
    }

    private void OnClearPreviousSectionData()
    {
        pendingClientTextForSection.Clear();
        pendingDatabaseTextForSection.Clear();

        activeClientOutputSection = null;
        activeOutputSection = null;
        lastSendToClient = DateTime.MinValue;
        lastSaveToDatabase = DateTime.MinValue;
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

    /// <summary>
    ///   Send realtime update to clients on the website
    /// </summary>
    /// <param name="message">The message to send</param>
    private async Task SendMessageToWebsiteClients(RealTimeBuildMessage message)
    {
        if (activeJob == null)
            throw new InvalidOperationException("There must be an active job to send a message to the website");

        var sender = AccessNotifications();

        await sender.Clients.Group(GetNotificationGroup(activeJob)).ReceiveNotification(new BuildMessageNotification
        {
            Message = message,
        });
    }

    private void ReleaseCurrentScope()
    {
        if (isScopeRefreshBlocked)
            throw new InvalidOperationException("Cannot release scope while it is blocked");

        database?.Dispose();
        database = null;
        notifications = null;
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

    private IHubContext<NotificationsHub, INotifications> AccessNotifications()
    {
        if (notifications != null)
            return notifications;

        AcquireScope();
        notifications =
            currentScope!.ServiceProvider.GetRequiredService<IHubContext<NotificationsHub, INotifications>>();
        return notifications;
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

    private class NewConnectionOpenedNotice(int connectionId, long runnerId)
    {
        public int ConnectionId { get; } = connectionId;
        public long RunnerId { get; } = runnerId;
    }
}
