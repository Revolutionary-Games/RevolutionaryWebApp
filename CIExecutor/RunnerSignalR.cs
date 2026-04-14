namespace CIExecutor;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using RevolutionaryWebApp.Server.Common.Services;

/// <summary>
///   Listens to SignalR messages from the server to know when we have new jobs.
/// </summary>
public class RunnerSignalR : IRunnerSignalService
{
    private readonly ILogger<RunnerSignalR> logger;
    private readonly HubConnection connection;

    private bool stopped;

    public RunnerSignalR(ILogger<RunnerSignalR> logger, IRunnerClientDataService dataService)
    {
        this.logger = logger;
        var hubUrl = dataService.ServerSignalUrl + $"?key={dataService.ConnectionKey}";

        connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new RunnerReconnectPolicy())
            .Build();

        connection.Reconnecting += error =>
        {
            Connected = false;
            this.logger.LogWarning(error, "SignalR connection lost. Reconnecting...");
            return Task.CompletedTask;
        };

        connection.Reconnected += async connectionId =>
        {
            Connected = true;
            this.logger.LogInformation("SignalR reconnected with connection id {ConnectionId}", connectionId);
            await OnConnectionEstablishedAsync();
        };

        connection.Closed += async error =>
        {
            Connected = false;

            if (stopped)
                return;

            this.logger.LogWarning(error, "SignalR connection closed unexpectedly. Restarting...");
            await TryStartWithReconnectLoopAsync(CancellationToken.None);
        };

        connection.On<string>(nameof(IRunnerNotifications.ReceiveNewJobNotice), async _ =>
        {
            try
            {
                if (!NewJobsReported)
                {
                    NewJobsReported = true;

                    logger.LogInformation("Got job notice, will wait a bit and process it");

                    // Based on priority, we sleep a random time here so that not all runners try to get the same job
                    // at the same time
                    await Task.Delay(new Random().Next(0, 1000 * Math.Clamp(OurPriority, 0, 15)));

                    OnNewJobsReported?.Invoke();
                    logger.LogInformation("Processed new job notice");
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to process new job notice");
            }
        });
    }

    public bool NewJobsReported { get; set; }

    public bool Connected { get; private set; }

    public int OurPriority { get; set; }

    public Action? OnNewJobsReported { get; set; }

    public async Task Start(CancellationToken cancellationToken)
    {
        stopped = false;
        logger.LogInformation("Starting SignalR connection");
        await TryStartWithReconnectLoopAsync(cancellationToken);

        logger.LogInformation("SignalR connection started");
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        stopped = true;

        if (connection.State != HubConnectionState.Disconnected)
        {
            await connection.StopAsync(cancellationToken);
            logger.LogInformation("SignalR connection stopped");
        }

        await connection.DisposeAsync();
        Connected = false;
    }

    private async Task TryStartWithReconnectLoopAsync(CancellationToken cancellationToken)
    {
        while (!stopped)
        {
            try
            {
                if (connection.State == HubConnectionState.Disconnected)
                {
                    await connection.StartAsync(cancellationToken);
                    Connected = true;
                    await OnConnectionEstablishedAsync();
                }

                cancellationToken.ThrowIfCancellationRequested();

                return;
            }
            catch (Exception e)
            {
                Connected = false;
                logger.LogWarning(e, "Failed to connect SignalR. Retrying...");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    /// <summary>
    ///   Runs every time the connection is established.
    /// </summary>
    private Task OnConnectionEstablishedAsync()
    {
        // With special runner notices, we don't need to actually want to join any groups here...

        // await connection.InvokeAsync("JoinGroup", NotificationGroups.RealtimeNewJobCreatedNotification);

        return Task.CompletedTask;
    }

    private sealed class RunnerReconnectPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var elapsed = retryContext.ElapsedTime;

            if (elapsed < TimeSpan.FromMinutes(1))
                return TimeSpan.FromSeconds(1);

            if (elapsed < TimeSpan.FromMinutes(10))
                return TimeSpan.FromSeconds(15);

            return TimeSpan.FromSeconds(30);
        }
    }
}
