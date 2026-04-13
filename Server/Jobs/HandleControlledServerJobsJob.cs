namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;
using Shared.Models;

/// <summary>
///   This job used to put tasks on servers, but now will just check if we have enough servers and start more if we
///   don't have enough.
/// </summary>
[DisableConcurrentExecution(60)]
public class HandleControlledServerJobsJob : IJob
{
    private readonly NotificationsEnabledDb database;
    private readonly RemoteServerHandler serverHandler;
    private readonly IBackgroundJobClient jobClient;

    public HandleControlledServerJobsJob(NotificationsEnabledDb database, RemoteServerHandler serverHandler,
        IBackgroundJobClient jobClient)
    {
        this.database = database;
        this.serverHandler = serverHandler;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var ciJobsNeedingActions = await database.CiJobs.Where(j => j.State != CIJobState.Finished)
            .ToListAsync(cancellationToken);

        await serverHandler.CheckServerStatuses(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        bool queuedRecheck = false;

        // Cancellation tokens are not used from here on out to avoid not saving changes
        // Start new servers if we have too many jobs
        await serverHandler.HandleCIJobs(ciJobsNeedingActions);

        // Skip this if we should cancel
        if (!cancellationToken.IsCancellationRequested)
            await serverHandler.ShutdownIdleServers();

        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();

        // If we have active servers, queue a recheck in 1 minute
        // So that we can turn them off once we have enough
        if (!queuedRecheck && (serverHandler.NewServersAdded || (await serverHandler.GetControlledServers()).Any(s =>
                s.Status is ServerStatus.Provisioning or ServerStatus.Running or ServerStatus.Stopping
                    or ServerStatus.WaitingForStartup)))
        {
            jobClient.Schedule<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None),
                TimeSpan.FromSeconds(60));
        }

        // Sleep a tiny amount to ensure that duplicate instances of this job can't hammer a server really hard
        await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
    }
}
