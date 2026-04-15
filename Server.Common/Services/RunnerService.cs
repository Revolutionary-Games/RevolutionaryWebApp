namespace RevolutionaryWebApp.Server.Common.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using SharedBase.Utilities;

/// <summary>
///   Main service that handles getting CI jobs and then running them and communicating with the server backend.
/// </summary>
public class RunnerService : IDisposable
{
    private readonly ILogger logger;
    private readonly IRunnerClientCommunication communication;
    private readonly IRunnerClientDataService dataService;
    private readonly IJobExecutor executor;
    private readonly IExecutorCache cache;
    private readonly IRunnerSignalService jobNotifications;

    private readonly SimpleJobOutputForwarder jobOutputForwarder;

    /// <summary>
    ///   Must be held when accessing the connection
    /// </summary>
    private readonly SemaphoreSlim connectionLock = new(1, 1);

    private readonly SemaphoreSlim queueLock = new(1, 1);

    /// <summary>
    ///   Used to make sure job messages don't get mixed up with each other when the send message queue is full.
    /// </summary>
    private readonly SemaphoreSlim jobOutputLock = new(1, 1);

    /// <summary>
    ///   Message queue for messages going out to the server
    /// </summary>
    private readonly Queue<RealTimeBuildMessage> outgoingMessages = new();

    private readonly SemaphoreSlim incomingMessageLock = new(1, 1);
    private readonly Queue<RealTimeBuildMessage> incomingMessages = new();

    private readonly CancellationTokenSource stopConnectionHandlingToken = new();
    private readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(60);

    private readonly TimeSpan jobAskInterval;

    private readonly PosixSignalRegistration? signalRegistration;

    private bool run = true;

    /// <summary>
    ///   Set to true once authenticated and the connection is open. Only once this is true can normal messages be
    ///   sent.
    /// </summary>
    private bool connectionIsSafeForNormalMessages;

    private bool serverPermanentlyLost;

    private bool canStartNewJobs = true;

    private bool quitAfterJob;
    private bool quitWhenIdle;

    private int idleLoops;

    private bool runConnectionHandling = true;

    private DateTime lastAskedForJobs = DateTime.MinValue;

    private DateTime lastHeartbeat = DateTime.UtcNow;

    private bool serverNotifiedAboutNewJobs;

    private long lastKnownCacheSize = -1;
    private bool cacheNearLimit;
    private int jobsSinceCacheSizeCheck;

    private bool hasCompletedAJobSinceLastCheck;

    private CIJobDTO? activeJob;
    private CiJobCacheConfigurationEnriched? jobCacheConfiguration;
    private bool runActiveJobOutput;

    private bool safeOnly;

    public RunnerService(ILogger logger, IRunnerClientCommunication communication, IRunnerClientDataService dataService,
        IJobExecutor executor, IExecutorCache cache, IRunnerSignalService jobNotifications, bool allowStopNewJobsSignal)
    {
        this.logger = logger;
        this.communication = communication;
        this.dataService = dataService;
        this.executor = executor;
        this.cache = cache;
        this.jobNotifications = jobNotifications;

        // Randomize ask delay a bit per runner to avoid a thundering herd problem
        jobAskInterval = TimeSpan.FromSeconds(180 + 120 * (float)new Random().NextDouble());

        jobOutputForwarder = new SimpleJobOutputForwarder(OnJobSectionClosed, OnJobSectionOpened, OnJobOutput);

        if (allowStopNewJobsSignal)
        {
            if (!OperatingSystem.IsWindows())
            {
                // Due to the limited set of signals supported here, we needed to pick something a bit silly
                signalRegistration = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, OnStopNewJobsSignal);
            }
            else
            {
                logger.LogInformation("Cannot register 'no new jobs' signal handler on Windows");
            }
        }

        jobNotifications.OnNewJobsReported = OnNewJobsReported;
    }

    public void StopAfterNextJob()
    {
        canStartNewJobs = false;
        quitAfterJob = true;
    }

    public void StopWhenIdle()
    {
        quitWhenIdle = true;
    }

    public void StopStartingNewJobs()
    {
        if (!canStartNewJobs)
            return;

        canStartNewJobs = false;
        logger.LogInformation("Received command to stop accepting new jobs, will no longer start new jobs");
    }

    public void OnlyRunSafeJobs()
    {
        if (safeOnly)
            return;

        safeOnly = true;
        logger.LogInformation("Only running safe jobs");
    }

    /// <summary>
    ///   Run this service until it should stop. The cancellation can be hooked up to receive a stop signal.
    /// </summary>
    /// <param name="cancellationToken">Cancellation to stop</param>
    /// <returns>Task that resolves to the process exit code</returns>
    public async Task<int> Run(CancellationToken cancellationToken)
    {
        try
        {
            // Set up a task that handles our connection state
            // And also outgoing messages to the server
            var connectionTask = HandleConnection(cancellationToken);

            var endConnectionAttempt = DateTime.UtcNow + TimeSpan.FromMinutes(10);

            logger.LogInformation("Beginning connection attempts, initial reads may fail for a little bit until the " +
                "socket is established");

            // We have to start reading things into the queue here as otherwise we won't reply to auth messages
            var readCancellation = new CancellationTokenSource();
            var readLinked = CancellationTokenSource.CreateLinkedTokenSource(readCancellation.Token, cancellationToken);
            var messageReadTask = ReadConnectionMessages(readLinked.Token);

            // Wait for initial connection
            while (true)
            {
                // We need to handle messages to do anything in general
                await GenericHandleOneServerMessage(cancellationToken);

                if (connectionIsSafeForNormalMessages)
                    break;

                if (DateTime.UtcNow > endConnectionAttempt)
                {
                    logger.LogError("Runner service failed to connect to the server at all");
                    return 4;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Cancelled when waiting for initial server connection");
                    cancellationToken.ThrowIfCancellationRequested();
                }

                if (serverPermanentlyLost || !canStartNewJobs || quitAfterJob)
                {
                    logger.LogError(
                        "Server connection permanently failed while waiting for initial connection, exiting");
                    return 6;
                }

                // Add a bit of extra delay if the server is not ready to accept us yet, we'd otherwise spam a ton of
                // error messages to our log
                await Task.Delay(200, cancellationToken);
            }

            logger.LogInformation("Successfully connected to the server. Starting main loop");

            while (run)
            {
                // Main state handling

                // Default state: waiting for jobs and periodically asking the server if there are any
                await HandleJobRequest(cancellationToken);

                if (activeJob != null)
                {
                    // When we have a job, we need to be in the job-run state
                    // As this is very important state, we want to actually crash if this throws
                    await HandleJobRun(cancellationToken);

                    // Clean cache data when we are using too much disk
                    await MaintainCache(cancellationToken);

                    logger.LogInformation("Finished performing job in the job run state");

                    // As we processed a job, we will want a new one right away
                    hasCompletedAJobSinceLastCheck = true;
                    idleLoops = 0;
                }
                else
                {
                    ++idleLoops;
                    bool anyMessages = false;

                    try
                    {
                        // Read messages to be empty
                        while (true)
                        {
                            var message = await ReceiveFromQueue(cancellationToken);

                            if (message == null)
                            {
                                if (!anyMessages)
                                    await Task.Delay(10, cancellationToken);
                                break;
                            }

                            message = await HandleCommonMessages(message);
                            anyMessages = true;

                            if (message != null)
                            {
                                logger.LogWarning("We got a message of type {Type} in idle state", message.Type);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to read server message while idle");
                        await Task.Delay(100, cancellationToken);
                    }

                    // Save extra CPU when the runner doesn't need to be doing anything
                    if (idleLoops > 20)
                    {
                        var extraWait = Math.Clamp(idleLoops * 10, 100, 1000);
                        await Task.Delay(extraWait, cancellationToken);
                    }
                }

                // Wait to save a bit of CPU when nothing is going on.
                // We don't want to have to wrap with a try-catch, so we just ignore cancellation.

                await Task.Delay(3, CancellationToken.None);

                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Runner service received cancellation request");
                    run = false;
                }

                if (quitAfterJob || (quitWhenIdle && idleLoops > 30))
                {
                    await WaitForMessageQueueToEmpty(cancellationToken);
                    run = false;
                }
            }

            runConnectionHandling = false;

            // Close connection first, so that we do the handshake with the server
            bool locked = await connectionLock.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            try
            {
                if (communication.IsConnected)
                    await communication.Close();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to do initial connection service disconnect " +
                    "(before readers were stopped)");
            }
            finally
            {
                if (locked)
                    connectionLock.Release();
            }

            if (!stopConnectionHandlingToken.IsCancellationRequested)
                await stopConnectionHandlingToken.CancelAsync();

            if (!readCancellation.IsCancellationRequested)
                await readCancellation.CancelAsync();

            try
            {
                await connectionTask;
            }
            catch (TaskCanceledException)
            {
                logger.LogInformation("Connection task cancelled");
            }

            try
            {
                await messageReadTask;
            }
            catch (TaskCanceledException)
            {
                logger.LogInformation("Read task cancelled");
            }

            try
            {
                if (communication.IsConnected)
                    await communication.Close();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to close connection when exiting runner service");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Runner service failed to run");
            return 3;
        }
        finally
        {
            runConnectionHandling = false;
        }

        if (serverPermanentlyLost)
        {
            logger.LogWarning("Server connection lost permanently");
            return 5;
        }

        return 0;
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
            bool unlock = connectionLock.Wait(TimeSpan.FromSeconds(5));
            try
            {
                if (communication.IsConnected)
                    communication.Close().Wait(TimeSpan.FromSeconds(15));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to close runner connection");
            }
            finally
            {
                if (unlock)
                    connectionLock.Release();
            }

            signalRegistration?.Dispose();

            connectionLock.Dispose();

            jobOutputForwarder.Dispose();

            queueLock.Dispose();
            stopConnectionHandlingToken.Dispose();
            jobOutputLock.Dispose();
            incomingMessageLock.Dispose();
        }
    }

    private async Task WaitForMessageQueueToEmpty(CancellationToken cancellationToken)
    {
        var timed = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timed.Token);

        while (true)
        {
            await queueLock.WaitAsync(linked.Token);
            try
            {
                if (outgoingMessages.Count == 0)
                    return;
            }
            finally
            {
                queueLock.Release();
            }

            await Task.Delay(100, linked.Token);
        }
    }

    private async Task<bool> SendMessage(RealTimeBuildMessage message, TimeSpan timeout)
    {
        if (!connectionIsSafeForNormalMessages)
        {
            logger.LogWarning("Connection likely doesn't accept our message of type {Type} yet", message.Type);
        }

        bool delay = false;

        var endBy = DateTime.UtcNow + timeout;

        while (true)
        {
            if (delay)
            {
                if (DateTime.UtcNow > endBy)
                {
                    logger.LogWarning("Timed out waiting for message queue to be available to send a message");
                    return false;
                }

                await Task.Delay(1000);
            }

            delay = true;

            if (!await queueLock.WaitAsync(timeout))
            {
                logger.LogError("Timed out waiting for message queue to be available to send message");
                return false;
            }

            try
            {
                // If queue size is too much, we need to wait
                if (outgoingMessages.Count > 10)
                {
                    logger.LogDebug("Outgoing message queue full, waiting...");
                    continue;
                }

                outgoingMessages.Enqueue(message);
                return true;
            }
            finally
            {
                queueLock.Release();
            }
        }
    }

    /// <summary>
    ///   Handles common message types and returns null, or if no inbuilt handling is done, the message is returned.
    /// </summary>
    /// <param name="message">Message to process</param>
    /// <returns>A non-null message if it isn't standard</returns>
    private async Task<RealTimeBuildMessage?> HandleCommonMessages(RealTimeBuildMessage? message)
    {
        if (message == null)
            return null;

        switch (message.Type)
        {
            // Note that some use cases must know if an error happened, so they need to check first for an error
            // before passing a message to this handler
            case BuildSectionMessageType.Error:
                logger.LogError("We received an error from the server: {Message}", message.ErrorMessage);
                return null;
            case BuildSectionMessageType.AuthDemand:
            {
                logger.LogInformation("Sending auth details to the server");

                // Send the response directly to bypass any queue and handling so that we can get the connection
                // established ASAP.
                if (!await ReplyToAuthDemand())
                {
                    logger.LogError("Failed to send auth response!");
                    connectionIsSafeForNormalMessages = false;
                }

                return null;
            }

            case BuildSectionMessageType.AuthSuccess:
            {
                logger.LogInformation("Server authenticated us successfully");
                connectionIsSafeForNormalMessages = true;

                var data = message.Output;

                if (data != null)
                {
                    var fields = data.Split(':');

                    if (fields.Length >= 2)
                    {
                        logger.LogInformation("Received our own info from the server: {Data}", data);
                        logger.LogInformation("Our runner ID is: {Id}", fields[0]);

                        if (int.TryParse(fields[1], out var priority))
                        {
                            logger.LogInformation("Our priority is: {Priority}", priority);
                            jobNotifications.OurPriority = priority;
                        }
                        else
                        {
                            logger.LogError("We couldn't parse our priority from: {Data}", fields[1]);
                        }
                    }
                    else
                    {
                        logger.LogError("Server sent us an invalid auth response (not enough fields)");
                    }
                }
                else
                {
                    logger.LogWarning("We didn't get our own info from the server");
                }

                return null;
            }

            case BuildSectionMessageType.HeartBeat:
                return null;
        }

        return message;
    }

    private async Task<RealTimeBuildMessage?> ReceiveFromQueue(CancellationToken cancellationToken)
    {
        await incomingMessageLock.WaitAsync(cancellationToken);
        try
        {
            if (incomingMessages.TryDequeue(out var message))
                return message;
        }
        finally
        {
            incomingMessageLock.Release();
        }

        return null;
    }

    /// <summary>
    ///   Wait for the next server message until cancelled (or the socket breaks). Don't use this directly as there's
    ///   a special task to do this!
    /// </summary>
    /// <returns>Server message or null</returns>
    private async Task<RealTimeBuildMessage?> Receive(CancellationToken cancellationToken)
    {
        try
        {
            var message = await communication.Receive(cancellationToken);

            return message;
        }
        catch (OperationCanceledException)
        {
            // Due to server heartbeats this shouldn't happen
            logger.LogWarning(
                "Timed out waiting for server message / we were interrupted, this likely closed the socket!");
            await Task.Delay(100, CancellationToken.None);
            return null;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to receive message");

            // Wait a bit so that the connection handler probably starts to do a reconnection
            await Task.Delay(150, CancellationToken.None);
            return null;
        }
    }

    /// <summary>
    ///   Handles sending messages and re-establishing the connection if it is lost.
    /// </summary>
    private async Task HandleConnection(CancellationToken overallCancellation)
    {
        var cancellationToken = stopConnectionHandlingToken.Token;

        int connectErrors = 0;

        bool expectingDisconnect = false;

        while (runConnectionHandling)
        {
            await connectionLock.WaitAsync(cancellationToken);

            bool connected = false;

            try
            {
                if (!communication.IsConnected)
                {
                    // Reset this flag once we see a disconnection
                    expectingDisconnect = false;
                    connectionIsSafeForNormalMessages = false;

                    if (serverPermanentlyLost || cancellationToken.IsCancellationRequested ||
                        overallCancellation.IsCancellationRequested)
                    {
                        logger.LogWarning("Server connection lost, stopping runner service");
                        run = false;
                        break;
                    }

                    var connectionTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var combined =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionTimeout.Token);

                    try
                    {
                        await communication.Connect(combined.Token);
                        expectingDisconnect = false;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to connect to server");
                        ++connectErrors;

                        if (connectErrors > 30)
                        {
                            logger.LogWarning("Server connection failed too many times");
                            serverPermanentlyLost = true;
                        }
                        else
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                        }
                    }
                }
                else
                {
                    connected = true;
                }

                if (connected)
                {
                    if (expectingDisconnect)
                    {
                        logger.LogWarning("Server did not disconnect when we were expecting it");
                        logger.LogInformation("Initiating reconnection attempt ourselves");

                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                        try
                        {
                            await communication.Close();
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning(e, "Failed to close connection before reconnecting");
                        }

                        // Re-use the above connection logic
                        continue;
                    }
                }
            }
            finally
            {
                connectionLock.Release();
            }

            if (connected && connectionIsSafeForNormalMessages)
            {
                // If time to send a heartbeat, send one now
                if (DateTime.UtcNow - lastHeartbeat > heartbeatInterval)
                {
                    if (!await SendMessage(new RealTimeBuildMessage
                        {
                            Type = BuildSectionMessageType.HeartBeat,
                        }, TimeSpan.FromSeconds(10)))
                    {
                        logger.LogError("Failed to send heartbeat");
                    }
                    else
                    {
                        lastHeartbeat = DateTime.UtcNow;
                    }
                }

                // We are connected so we can process the message queue
                int messagesToSend = 5;

                await queueLock.WaitAsync(cancellationToken);
                try
                {
                    while (outgoingMessages.TryPeek(out var message))
                    {
                        // This may throw so we only dequeue after success
                        await communication.Send(message, cancellationToken);

                        outgoingMessages.Dequeue();

                        if (--messagesToSend <= 0)
                            break;

                        // Succeeded sending
                        connectErrors = Math.Max(0, connectErrors - 3);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to process message queue");

                    // It's not safe to send normal messages any more, so we need to wait a bit before trying again
                    connectionIsSafeForNormalMessages = false;
                    expectingDisconnect = true;
                    await Task.Delay(100, cancellationToken);
                }
                finally
                {
                    queueLock.Release();
                }
            }

            try
            {
                await Task.Delay(10, cancellationToken);
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    /// <summary>
    ///   Reads connection messages while the runner service is running. And makes them available in a queue.
    /// </summary>
    /// <param name="cancellationToken">Cancellation of the operation (will close the socket!)</param>
    private async Task ReadConnectionMessages(CancellationToken cancellationToken)
    {
        logger.LogInformation("Beginning to read server messages");

        bool? connectionState = null;
        bool firstMessage = false;

        while (runConnectionHandling && !serverPermanentlyLost)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Get our own connection state variable before reading
            if (connectionState == null)
            {
                await connectionLock.WaitAsync(cancellationToken);
                try
                {
                    connectionState = communication.IsConnected;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Failed to read connection state");
                    await Task.Delay(100, cancellationToken);
                    continue;
                }
                finally
                {
                    connectionLock.Release();
                }
            }

            if (connectionState == false)
            {
                // Wait for a connection to be established before reading anything
                await Task.Delay(100, cancellationToken);
                connectionState = null;
                continue;
            }

            RealTimeBuildMessage? message = null;
            try
            {
                message = await Receive(cancellationToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to read server message");
                connectionState = null;

                // Wait a bit before trying again to not print errors too fast
                await Task.Delay(500, cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                if (message != null)
                    logger.LogWarning("We got a message of type {Type} while read is cancelled", message.Type);
                break;
            }

            if (message == null)
                continue;

            if (firstMessage)
            {
                firstMessage = false;
                logger.LogInformation("Received first message from the server and put in queue");
            }

            await incomingMessageLock.WaitAsync(cancellationToken);
            try
            {
                incomingMessages.Enqueue(message);

                if (incomingMessages.Count > 1000)
                {
                    logger.LogWarning("Incoming message queue is full, dropping some messages");

                    int dropped = 0;

                    while (incomingMessages.Count > 1000)
                    {
                        var tooLongMessage = incomingMessages.Dequeue();

                        tooLongMessage = await HandleCommonMessages(tooLongMessage);

                        if (tooLongMessage != null)
                        {
                            logger.LogWarning("Dropped a message of type {Type} from incoming message queue",
                                tooLongMessage);
                        }

                        if (++dropped > 100)
                            break;
                    }
                }
            }
            finally
            {
                incomingMessageLock.Release();
            }
        }

        logger.LogInformation("Exiting server message reader loop");
    }

    private async Task<bool> ReplyToAuthDemand()
    {
        var authResponse = new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.AuthResponse,
            Output = dataService.SecretKey,
        };
        var absoluteCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        try
        {
            while (true)
            {
                await connectionLock.WaitAsync(absoluteCancellation.Token);

                try
                {
                    await communication.Send(authResponse, absoluteCancellation.Token);
                    logger.LogInformation("Successfully sent auth response to the server");
                    return true;
                }
                finally
                {
                    connectionLock.Release();
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to send auth response");
            return false;
        }
    }

    private async Task HandleJobRequest(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        // We have a bunch of conditions here to ensure we don't ask the server too often for jobs, but that we do
        // ask the server for new jobs if we have completed a job since the last check to run them quickly
        var lastAsked = now - lastAskedForJobs;
        if (canStartNewJobs && (lastAsked > jobAskInterval ||
                (serverNotifiedAboutNewJobs && lastAsked > TimeSpan.FromSeconds(10)) || hasCompletedAJobSinceLastCheck))
        {
            // Ask for jobs from the server
            lastAskedForJobs = now;
            hasCompletedAJobSinceLastCheck = false;

            if (serverNotifiedAboutNewJobs)
            {
                logger.LogInformation("We reacted to notice about new jobs by asking for them");
                serverNotifiedAboutNewJobs = false;
            }
            else
            {
                logger.LogInformation("Asking for new jobs from the server (last asked {TimeAgo})", lastAsked);
            }

            if (!await SendMessage(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.GetAvailableJobs,
                }, TimeSpan.FromSeconds(10)))
            {
                logger.LogError("Failed to send GetAvailableJobs message");
                return;
            }
        }
        else
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        if (!canStartNewJobs)
            throw new Exception("Shouldn't get here with start new jobs flag disabled!");

        var endBy = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (true)
        {
            var reply = await ReceiveFromQueue(cancellationToken);

            // Don't get to an infinite loop on cancellation
            if (reply == null && cancellationToken.IsCancellationRequested)
                return;

            if (reply?.Type == BuildSectionMessageType.Error)
            {
                logger.LogError("We received an error when asking for jobs. Are we not authenticated? {@E}", reply);
                await Task.Delay(200, cancellationToken);
                continue;
            }

            reply = await HandleCommonMessages(reply);

            // We got no info
            if (reply == null && DateTime.UtcNow > endBy)
            {
                logger.LogError("We expected to get some job info from the server, but didn't");
                return;
            }

            if (reply == null)
            {
                // No reply yet, but not time to quit either, sleep a bit and then check again in the queue
                await Task.Delay(10, cancellationToken);
                continue;
            }

            if (reply.Type == BuildSectionMessageType.JobsList)
            {
                // Parse the job list and see if there's anything we like

                AvailableJobsList jobs;
                try
                {
                    jobs = JsonSerializer.Deserialize<AvailableJobsList>(reply.Output ??
                            throw new Exception("Missing output in message")) ??
                        throw new NullDecodedJsonException();

                    if (safeOnly)
                    {
                        var oldCount = jobs.Jobs.Count;
                        jobs.Jobs = jobs.Jobs.Where(j =>
                            j.RequiredRunnerTags != null && j.RequiredRunnerTags.Split(';').Contains("safe")).ToList();

                        var newCount = jobs.Jobs.Count;
                        logger.LogInformation(
                            $"Filtered {oldCount - newCount} jobs to safe only mode. {newCount} left to consider.");
                        jobs.FilteredCount += oldCount - newCount;
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to parse jobs list");
                    return;
                }

                if (jobs.Jobs.Count < 1)
                {
                    // No jobs, time to go back to just waiting for jobs to appear
                    return;
                }

                List<CIJobDTO> potentialJobs = [jobs.Jobs.First()];
                var rawCount = jobs.Jobs.Count;

                if (rawCount > 1)
                {
                    jobs.Jobs.RemoveAt(0);

                    // Then pick another random job to try in case multiple servers get the same job idea at the same
                    // time
                    potentialJobs.Add(jobs.Jobs[new Random().Next(jobs.Jobs.Count)]);
                }

                try
                {
                    logger.LogInformation("We will try to start a job...");
                    if (!await TryToStartJob(potentialJobs, cancellationToken))
                    {
                        logger.LogError("Failed to start a job after getting {Count} jobs from the server",
                            rawCount);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to start a job after getting {Count} jobs from the server",
                        rawCount);
                }

                return;
            }

            if (reply.Type == BuildSectionMessageType.ActiveJobDetails)
            {
                // We already have a job we should run, so switch to it
                logger.LogInformation("Server told us that there's a job we should be running already, " +
                    "however we are in a state where we no longer have the job data");

                // We don't actually need to know anything about the job, so we just cancel here

                // We can't send this as we don't know the active section...
                /*if (!await SendMessage(new RealTimeBuildMessage
                    {
                        Output = "This runner had restarted and no longer knows about this job\n",
                        Type = BuildSectionMessageType.BuildOutput,
                    }, TimeSpan.FromSeconds(60)))
                {
                    logger.LogError("We failed to tell the server we cannot work on a job anymore");
                }*/

                // So we want to tell the server that we cannot perform the job
                if (!await SendMessage(new RealTimeBuildMessage
                    {
                        WasSuccessful = false,
                        Type = BuildSectionMessageType.FinalStatus,
                    }, TimeSpan.FromSeconds(60)))
                {
                    logger.LogError("We failed to tell the server we cannot work on a job anymore");
                }

                // TODO: should we immediately go asking for a new job or just wait for the regular poll?
                return;
            }

            logger.LogWarning("We got an unexpected message of type in idle state: {Type}", reply.Type);

            if (cancellationToken.IsCancellationRequested)
                return;
        }
    }

    private async Task<bool> TryToStartJob(List<CIJobDTO> potentialJobs, CancellationToken cancellationToken)
    {
        var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var cancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);

        // Request task start one by one and listen for messages from the server whether we got the job or there's
        // an error.
        foreach (var potentialJob in potentialJobs)
        {
            if (activeJob != null)
                throw new InvalidOperationException("We already have an active job");

            await SendMessage(new RealTimeBuildMessage
            {
                Type = BuildSectionMessageType.RequestStartJob,
                Output = $"{potentialJob.CiProjectId}:{potentialJob.CiBuildId}:{potentialJob.CiJobId}",
            }, TimeSpan.FromSeconds(60));

            while (true)
            {
                // DO NOT check the can start jobs here as some tests rely on giving the list first and then
                // disallowing further jobs, so if we get this far, we want to start a job even if someone "just" has
                // wanted us to stop.
                if (cancellation.IsCancellationRequested)
                    break;

                var message = await ReceiveFromQueue(cancellation.Token);

                if (message == null)
                {
                    // Wait a bit before checking the queue again to save on CPU power
                    await Task.Delay(10, cancellation.Token);
                    continue;
                }

                if (message.Type == BuildSectionMessageType.Error)
                {
                    logger.LogInformation("We got error from server (likely we couldn't get the job): {Message}",
                        message.ErrorMessage);

                    // Still try other jobs
                    continue;
                }

                message = await HandleCommonMessages(message);

                if (message == null)
                    continue;

                if (message.Type == BuildSectionMessageType.ActiveJobDetails)
                {
                    logger.LogInformation("Server told us a job, we will start running it now");

                    try
                    {
                        var data = JsonSerializer.Deserialize<RunningJobDetails>(message.Output ??
                                throw new Exception("Missing output in message")) ??
                            throw new NullDecodedJsonException();

                        activeJob = data.GeneralDetails;
                        jobCacheConfiguration = data.CacheConfiguration;
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Failed to start job due to data parsing");
                        throw new Exception("Invalid job data to start", e);
                    }

                    logger.LogInformation("Our new Job is: {Project}:{Build}:{Job}", activeJob.CiProjectId,
                        activeJob.CiBuildId, activeJob.CiJobId);
                    return true;
                }

                logger.LogWarning("We got a message of type {Type} in waiting for job start response",
                    message.Type);
            }
        }

        return false;
    }

    private async Task GenericHandleOneServerMessage(CancellationToken cancellationToken)
    {
        try
        {
            var message = await ReceiveFromQueue(cancellationToken);

            message = await HandleCommonMessages(message);

            if (message != null)
            {
                logger.LogWarning("We got a message of type {Type} in general state that we are ignoring",
                    message.Type);
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to receive message");
        }
    }

    private async Task HandleJobRun(CancellationToken cancellationToken)
    {
        if (jobCacheConfiguration == null || activeJob == null)
            throw new InvalidOperationException("We don't have a job to run");

        // Start a reader task that just ignores most messages
        var readCancellation = new CancellationTokenSource(AppInfo.MaxCIJobExecutorTime * 2);

        var runCancellationTime = new CancellationTokenSource(AppInfo.MaxCIJobExecutorTime);
        var runCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(runCancellationTime.Token, cancellationToken);

        var totalCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, readCancellation.Token);

        await jobOutputForwarder.OnNewJobStarted();

        runActiveJobOutput = true;
        var readTask = ReadMessagesInRun(totalCancellation.Token);

        var result = await executor.ExecuteJobAsync(jobCacheConfiguration, activeJob, dataService, jobOutputForwarder,
            cache, runCancellation.Token);

        // TODO: when the job is complete should wait until the outgoing message queue is empty before putting
        // the final job status message to the queue to give time for final messages to get out of the process

        runActiveJobOutput = false;
        await readCancellation.CancelAsync();

        var finalWait = new CancellationTokenSource(TimeSpan.FromMinutes(3));

        // When the job is done, we wait for a little bit to report the final state to make sure the queue is empty
        try
        {
            await WaitForMessageQueueToEmpty(finalWait.Token);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to wait for message queue to empty after a job");
        }

        // And as the last thing we report the job status
        try
        {
            if (!await SendMessage(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.FinalStatus,
                    WasSuccessful = result,
                }, TimeSpan.FromMinutes(15)))
            {
                throw new Exception("Failed to send final status message after waiting 15 minutes");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to report final job result");
        }

        // And then we clear the active job
        activeJob = null;
        jobCacheConfiguration = null;

        // TODO: this is now a bit cumbersome design, so we probably could have just a persistent read task that puts
        // important messages to a local queue that can be read when needed
        logger.LogInformation("Waiting for read message loop to complete");
        await readTask;

        logger.LogInformation("Finalized running a task, returning to normal state");
    }

    private async Task OnJobOutput(string section, int sectionId, string output)
    {
        var message = new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.BuildOutput,
            Output = output,
            SectionId = sectionId,
            SectionName = section,
        };

        // We need a second level lock around SendMessage as otherwise build messages might get intermixed
        // if the queue is full
        if (!await jobOutputLock.WaitAsync(TimeSpan.FromMinutes(10)))
        {
            logger.LogWarning("Timed out waiting to output: {Output}", output);
            return;
        }

        try
        {
            if (!await SendMessage(message, TimeSpan.FromMinutes(9)))
            {
                logger.LogError("Failed to send output message: {Output}", output);
            }
        }
        finally
        {
            jobOutputLock.Release();
        }
    }

    private async Task OnJobSectionOpened(string section, int sectionId)
    {
        var message = new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.SectionStart,
            SectionId = sectionId,
            SectionName = section,
        };

        if (!await jobOutputLock.WaitAsync(TimeSpan.FromMinutes(14)))
        {
            logger.LogWarning("Timed out waiting to start section: {Section}", section);
            return;
        }

        try
        {
            if (!await SendMessage(message, TimeSpan.FromMinutes(13)))
            {
                logger.LogError("Failed to send section start message: {Section}", section);
            }
        }
        finally
        {
            jobOutputLock.Release();
        }
    }

    private async Task OnJobSectionClosed(string section, int sectionId, bool success)
    {
        var message = new RealTimeBuildMessage
        {
            Type = BuildSectionMessageType.SectionEnd,
            SectionId = sectionId,
            SectionName = section,
            WasSuccessful = success,
        };

        if (!await jobOutputLock.WaitAsync(TimeSpan.FromMinutes(12)))
        {
            logger.LogWarning("Timed out waiting to end section: {Section} ({Success})", section, success);
            return;
        }

        try
        {
            if (!await SendMessage(message, TimeSpan.FromMinutes(11)))
            {
                logger.LogError("Failed to send section end section: {Section} ({Success})", section, success);
            }
        }
        finally
        {
            jobOutputLock.Release();
        }
    }

    private async Task ReadMessagesInRun(CancellationToken cancellationToken)
    {
        while (runActiveJobOutput)
        {
            try
            {
                // There aren't any important messages we will listen to during jobs, so we just want to keep the
                // buffer empty
                var message = await ReceiveFromQueue(cancellationToken);

                message = await HandleCommonMessages(message);

                if (message != null)
                {
                    logger.LogWarning("We got a message of type {Type} in job run state that we are ignoring",
                        message.Type);
                }

                cancellationToken.ThrowIfCancellationRequested();

                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Exiting read messages task for active job");
                return;
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to receive message");
            }
        }
    }

    private async Task MaintainCache(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();

        // Calculate the cache size if it needs to be known
        if (jobsSinceCacheSizeCheck >= 3 || lastKnownCacheSize == -1 || cacheNearLimit)
        {
            lastKnownCacheSize = await cache.CalculateCacheSizeAsync(cancellationToken);
            logger.LogInformation("Current cache size: {Size:F1} MiB",
                (float)lastKnownCacheSize / GlobalConstants.MEBIBYTE);
            jobsSinceCacheSizeCheck = 0;
        }
        else
        {
            ++jobsSinceCacheSizeCheck;
        }

        if (lastKnownCacheSize >= dataService.MaxCacheSize * dataService.PruneCacheAfterSizeFraction)
        {
            logger.LogInformation("Cache is near limit, pruning...");
            lastKnownCacheSize = await cache.PruneCacheAsync(dataService.KeepCacheSize, cancellationToken);
        }

        // Remember to check cache often if we are near the limit
        cacheNearLimit = lastKnownCacheSize >
            dataService.MaxCacheSize * (dataService.PruneCacheAfterSizeFraction - 0.1f);

        var elapsed = timer.Elapsed;
        if (elapsed.TotalSeconds > 10)
        {
            logger.LogInformation("Cache maintenance took {Elapsed} seconds", elapsed.TotalSeconds);
        }
    }

    private void OnStopNewJobsSignal(PosixSignalContext context)
    {
        logger.LogInformation("Caught SIGWINCH, will stop accepting jobs. Sorry about this signal abuse but .NET " +
            "doesn't expose SIGUSR1");
        logger.LogInformation("To disable this behaviour, run with '--interactive' flag");
        StopStartingNewJobs();
        context.Cancel = true;
    }

    private void OnNewJobsReported()
    {
        if (!canStartNewJobs)
            return;

        serverNotifiedAboutNewJobs = true;
        logger.LogInformation("Server notified us that there are new jobs, so we notified the main loop");
    }
}
