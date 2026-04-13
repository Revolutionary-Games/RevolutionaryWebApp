namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Models;

/// <summary>
///   Run maintenance on the controlled and external servers that are marked as wanting maintenance
///   (and aren't currently running anything)
/// </summary>
[DisableConcurrentExecution(1200)]
public class RunMarkedServerMaintenanceJob : IJob
{
    private readonly ILogger<RunMarkedServerMaintenanceJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;
    private readonly IEC2Controller ec2Controller;

    public RunMarkedServerMaintenanceJob(ILogger<RunMarkedServerMaintenanceJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient, IEC2Controller ec2Controller)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
        this.ec2Controller = ec2Controller;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        // TODO: if this code is kept, we need to notify the runner on the server to not accept new jobs before we
        // can safely shut it down
        var controlled = await database.ControlledServers.Where(s =>
            s.WantsMaintenance &&
            (s.Status == ServerStatus.Running || s.Status == ServerStatus.Stopped)).ToListAsync(cancellationToken);

        foreach (var server in controlled)
        {
            if (string.IsNullOrEmpty(server.InstanceId))
                throw new InvalidOperationException("Server that wants maintenance has no instance id set");

            await ec2Controller.TerminateInstance(server.InstanceId);
            server.Status = ServerStatus.Terminated;
            server.StatusLastChecked = DateTime.UtcNow;
            server.LastMaintenance = DateTime.UtcNow;
            server.CleanUpQueued = false;
            server.WantsMaintenance = false;
            server.BumpUpdatedAt();

            // No cancellation token as we have already terminated it
            // ReSharper disable once MethodSupportsCancellation
            await database.SaveChangesAsync();

            // In order to keep down the max time this job might run only the first server of each type is handled
            // at once
            break;
        }

        cancellationToken.ThrowIfCancellationRequested();

        // If there are servers that need maintenance *now* run this job again
        if (controlled.Count > 1)
        {
            logger.LogInformation("There are more servers that want maintenance right now, re-scheduling this job");
            jobClient.Schedule<RunMarkedServerMaintenanceJob>(x => x.Execute(CancellationToken.None),
                TimeSpan.FromSeconds(15));
        }
    }
}
