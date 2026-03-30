namespace RevolutionaryWebApp.Server.Common.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Models;
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

    /// <summary>
    ///   Must be held when accessing the connection
    /// </summary>
    private readonly SemaphoreSlim connectionLock = new(1, 1);

    private readonly SemaphoreSlim queueLock = new(1, 1);

    /// <summary>
    ///   Message queue for messages going out to the server
    /// </summary>
    private readonly Queue<RealTimeBuildMessage> outgoingMessages = new();

    private readonly CancellationTokenSource stopConnectionHandlingToken = new();
    private readonly TimeSpan heartbeatInterval = TimeSpan.FromSeconds(60);

    private bool run = true;

    /// <summary>
    ///   Set to true once authenticated and the connection is open. Only once this is true can normal messages be
    ///   sent.
    /// </summary>
    private bool connectionIsSafeForNormalMessages;

    private bool serverPermanentlyLost;

    private bool canStartNewJobs = true;

    private bool quitAfterJob;

    private bool runConnectionHandling = true;

    private DateTime lastAskedForJobs = DateTime.MinValue;

    private DateTime lastHeartbeat = DateTime.UtcNow;

    private bool serverNotifiedAboutNewJobs;

    private CIJobDTO? activeJob;
    private CiJobCacheConfiguration? jobCacheConfiguration;

    // TODO: listen for USR1 signal and if received then stop the new jobs allowed flag
    // TODO: sigint should do the same, but also stop the run loop once there's no job anymore

    public RunnerService(ILogger logger, IRunnerClientCommunication communication, IRunnerClientDataService dataService)
    {
        this.logger = logger;
        this.communication = communication;
        this.dataService = dataService;
    }

    public void StopAfterNextJob()
    {
        canStartNewJobs = false;
        quitAfterJob = true;
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
            var connectionTask = HandleConnection();

            var endConnectionAttempt = DateTime.UtcNow + TimeSpan.FromMinutes(10);

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
            }

            while (run)
            {
                // Main state handling

                // Default state: waiting for jobs and periodically asking the server if there are any
                await HandleJobRequest(cancellationToken);

                if (activeJob != null)
                {
                    // When we have a job, we need to be in the job-run state
                    await HandleJobRun(cancellationToken);
                    logger.LogInformation("Finished performing job in job run state");

                    // As we processed a job, we will want a new one right away
                    serverNotifiedAboutNewJobs = true;
                }

                // Wait to save a bit of CPU when nothing is going on.
                // We don't want to have to wrap with a try-catch, so we just ignore cancellation.

                await Task.Delay(3, CancellationToken.None);

                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Runner service received cancellation request");
                    run = false;
                }

                if (quitAfterJob)
                {
                    await WaitForMessageQueueToEmpty(cancellationToken);
                    run = false;
                }
            }

            runConnectionHandling = false;
            if (!stopConnectionHandlingToken.IsCancellationRequested)
                await stopConnectionHandlingToken.CancelAsync();

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

            connectionLock.Dispose();

            queueLock.Dispose();
            stopConnectionHandlingToken.Dispose();
        }
    }

    private async Task WaitForMessageQueueToEmpty(CancellationToken cancellationToken)
    {
        var timed = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timed.Token);

        while (true)
        {
            await queueLock.WaitAsync(cancellationToken);
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
                    logger.LogDebug("Queue full, waiting...");
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

                return null;
            }

            case BuildSectionMessageType.HeartBeat:
                return null;

            case BuildSectionMessageType.NewJobsAvailable:
                serverNotifiedAboutNewJobs = true;
                return null;
        }

        return message;
    }

    /// <summary>
    ///   Wait for the next server message for a specified time or until cancellation.
    /// </summary>
    /// <returns>Server message or null</returns>
    private async Task<RealTimeBuildMessage?> Receive(CancellationToken cancellationToken,
        TimeSpan maxWaitTime = default)
    {
        if (maxWaitTime == TimeSpan.Zero)
            maxWaitTime = TimeSpan.FromSeconds(60);

        try
        {
            var message = await communication.Receive(maxWaitTime, cancellationToken);

            return message;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to receive message");

            // Wait a bit so that the connection handler probably starts to do a reconnection
            await Task.Delay(100, CancellationToken.None);
            return null;
        }
    }

    private async Task HandleConnection()
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
                    connectionIsSafeForNormalMessages = false;

                    if (serverPermanentlyLost)
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
        // If we don't want new jobs, don't do this
        if (!canStartNewJobs)
        {
            return;
        }

        var now = DateTime.UtcNow;

        var lastAsked = now - lastAskedForJobs;
        if (lastAsked > TimeSpan.FromSeconds(500) ||
            (serverNotifiedAboutNewJobs && lastAsked > TimeSpan.FromSeconds(10)))
        {
            // Ask for jobs from the server
            lastAskedForJobs = now;
            if (!await SendMessage(new RealTimeBuildMessage
                {
                    Type = BuildSectionMessageType.GetAvailableJobs,
                }, TimeSpan.FromSeconds(10)))
            {
                logger.LogError("Failed to send GetAvailableJobs message");
                return;
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return;

        while (true)
        {
            var reply = await Receive(cancellationToken, TimeSpan.FromSeconds(30));

            reply = await HandleCommonMessages(reply);

            // We got no info
            if (reply == null)
            {
                logger.LogError("We expected to get some job info from the server, but didn't");
                return;
            }

            if (reply.Type == BuildSectionMessageType.JobsList)
            {
                // Parse the jobs list and see if there's anything we like

                AvailableJobsList jobs;
                try
                {
                    jobs = JsonSerializer.Deserialize<AvailableJobsList>(reply.Output ??
                            throw new Exception("Missing output in message")) ??
                        throw new NullDecodedJsonException();
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

                if (!await TryToStartJob(potentialJobs, cancellationToken))
                {
                    logger.LogError("Failed to start a job after getting {Count} jobs from the server",
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

            if (reply.Type == BuildSectionMessageType.Error)
            {
                logger.LogError("We received an error when asking for jobs. Are we not authenticated? {@E}", reply);
            }
            else
            {
                logger.LogWarning("We got an unexpected message of type in idle state: {Type}", reply.Type);
            }

            if (cancellationToken.IsCancellationRequested)
                return;
        }
    }

    private async Task<bool> TryToStartJob(List<CIJobDTO> potentialJobs, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    private async Task GenericHandleOneServerMessage(CancellationToken cancellationToken, TimeSpan maxWait = default)
    {
        if (maxWait == TimeSpan.Zero)
            maxWait = TimeSpan.FromSeconds(15);

        try
        {
            var message = await Receive(cancellationToken, maxWait);

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
        // Start a reader task that just ignores most messages

        throw new NotImplementedException();

        // We need a second level lock around SendMessage as otherwise build messages might get intermixed if the queue is full

        // TODO: when the job is complete should wait until the outgoing message queue is empty before putting
        // the final job status message to the queue to give time for final messages to get out of the process
    }
}
