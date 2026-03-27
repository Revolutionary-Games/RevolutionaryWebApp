namespace RevolutionaryWebApp.Server.Common.Services;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

/// <summary>
///   Main service that handles getting CI jobs and then running them and communicating with the server backend.
/// </summary>
public class RunnerService : IDisposable
{
    private readonly ILogger logger;
    private readonly IRunnerClientCommunication communication;

    private bool run = true;

    public RunnerService(ILogger logger, IRunnerClientCommunication communication)
    {
        this.logger = logger;
        this.communication = communication;
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
            // TODO: socket setup and connection

            // TODO: connection state management task

            while (run)
            {
                // TODO: main states: waiting for jobs, and running a job

                // Wait to save a bit of CPU when nothing is going on.
                // We don't want to have to try-catch this, so we just ignore cancellation.

                // ReSharper disable once MethodSupportsCancellation
                await Task.Delay(1);

                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Runner service received cancellation request");
                    run = false;
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Runner service failed to run");
            return 3;
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
            try
            {
                if (communication.IsConnected)
                    communication.Close().Wait(TimeSpan.FromSeconds(15));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to close runner connection");
            }
        }
    }
}
