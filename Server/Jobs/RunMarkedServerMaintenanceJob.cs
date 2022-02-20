namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
public class RunMarkedServerMaintenanceJob : IJob
{
    private const string ExternalServerMaintenanceCommand = "sudo dnf update -y --refresh";

    private readonly ILogger<RunMarkedServerMaintenanceJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;
    private readonly IEC2Controller ec2Controller;
    private readonly IExternalServerSSHAccess serverSSHAccess;

    public RunMarkedServerMaintenanceJob(ILogger<RunMarkedServerMaintenanceJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient, IEC2Controller ec2Controller, IExternalServerSSHAccess serverSSHAccess)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
        this.ec2Controller = ec2Controller;
        this.serverSSHAccess = serverSSHAccess;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var controlled = await database.ControlledServers.Where(s =>
            s.WantsMaintenance && s.ReservationType == ServerReservationType.None &&
            (s.Status == ServerStatus.Running || s.Status == ServerStatus.Stopped)).ToListAsync(cancellationToken);

        foreach (var server in controlled)
        {
            if (string.IsNullOrEmpty(server.InstanceId))
                throw new InvalidOperationException("Server that wants maintenance has no instance id set");

            await ec2Controller.TerminateInstance(server.InstanceId);
            server.Status = ServerStatus.Terminated;
            server.StatusLastChecked = DateTime.UtcNow;
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

        var external = await database.ExternalServers.Where(s =>
                s.WantsMaintenance && s.ReservationType == ServerReservationType.None &&
                s.Status == ServerStatus.Running)
            .ToListAsync(cancellationToken);

        foreach (var server in external)
        {
            if (server.PublicAddress == null)
                throw new InvalidOperationException("Server that wants maintenance has no public address set");

            try
            {
                serverSSHAccess.ConnectTo(server.PublicAddress.ToString(), server.SSHKeyFileName);
                var command = serverSSHAccess.RunCommand(ExternalServerMaintenanceCommand);

                if (!command.Success)
                {
                    throw new Exception($"Running commands through SSH failed: {command.Error}, {command.Result}");
                }

                logger.LogInformation("Output for external server maintenance ({Id}): {Result}", server.Id,
                    command.Result);

                serverSSHAccess.Reboot();
            }
            catch (Exception e)
            {
                throw new Exception($"Failed to perform maintenance on external server {server.Id} due to exception",
                    e);
            }

            server.Status = ServerStatus.Stopping;
            server.StatusLastChecked = DateTime.UtcNow;
            server.WantsMaintenance = false;
            server.BumpUpdatedAt();

            // No cancellation token as we have already restarted the server so the state must be stored
            // ReSharper disable once MethodSupportsCancellation
            await database.SaveChangesAsync();

            jobClient.Schedule<WaitForExternalServerStartUpJob>(x => x.Execute(server.Id, CancellationToken.None),
                TimeSpan.FromSeconds(20));

            break;
        }

        // If there are servers that need maintenance *now* run this job again
        if (controlled.Count > 1 || external.Count > 1)
        {
            logger.LogInformation("There are more servers that want maintenance right now, re-scheduling this job");
            jobClient.Schedule<RunMarkedServerMaintenanceJob>(x => x.Execute(CancellationToken.None),
                TimeSpan.FromSeconds(15));
        }
    }
}
