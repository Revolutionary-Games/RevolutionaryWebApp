namespace RevolutionaryWebApp.Server.Common.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///   Allows realtime receiving of server data to know when there are new jobs
/// </summary>
public interface IRunnerSignalService
{
    public bool NewJobsReported { get; }

    public bool Connected { get; }

    public int OurPriority { get; set; }

    public Action? OnNewJobsReported { get; set; }

    public Task Start(CancellationToken cancellationToken);

    public Task Stop(CancellationToken cancellationToken);
}

/// <summary>
///   A signal service that does nothing.
/// </summary>
public class DummyRunnerSignalService : IRunnerSignalService
{
    public bool NewJobsReported { get; set; }
    public bool Connected { get; set; }
    public int OurPriority { get; set; }
    public Action? OnNewJobsReported { get; set; }

    public Task Start(CancellationToken cancellationToken)
    {
        Connected = true;
        return Task.CompletedTask;
    }

    public Task Stop(CancellationToken cancellationToken)
    {
        Connected = false;
        return Task.CompletedTask;
    }
}
