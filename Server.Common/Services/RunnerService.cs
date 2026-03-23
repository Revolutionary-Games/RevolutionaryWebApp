namespace RevolutionaryWebApp.Server.Common.Services;

using System;
using Microsoft.Extensions.Logging;

/// <summary>
///   Main service that handles getting CI jobs and then running them and communicating with the server backend.
/// </summary>
public class RunnerService : IDisposable
{
    private readonly ILogger logger;
    private readonly IRunnerClientCommunication communication;

    public RunnerService(ILogger logger, IRunnerClientCommunication communication)
    {
        this.logger = logger;
        this.communication = communication;
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
                communication.Close().Wait(TimeSpan.FromSeconds(15));
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to close runner connection");
            }
        }
    }
}
