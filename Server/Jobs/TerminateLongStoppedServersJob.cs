namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Models;

    public class TerminateLongStoppedServersJob : IJob
    {
        private readonly TimeSpan terminateStoppedServerDelay = TimeSpan.FromDays(3);

        private readonly ILogger<TerminateLongStoppedServersJob> logger;
        private readonly ApplicationDbContext database;
        private readonly IEC2Controller ec2Controller;

        public TerminateLongStoppedServersJob(ILogger<TerminateLongStoppedServersJob> logger,
            ApplicationDbContext database, IEC2Controller ec2Controller)
        {
            this.logger = logger;
            this.database = database;
            this.ec2Controller = ec2Controller;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            if (!ec2Controller.Configured)
            {
                logger.LogInformation(
                    "Skipping terminate long stopped servers job as ec2 controller is not configured");
                return;
            }

            var cutoff = DateTime.UtcNow - terminateStoppedServerDelay;

            var servers = await database.ControlledServers.AsQueryable()
                .Where(s => s.Status == ServerStatus.Stopped && s.UpdatedAt < cutoff).ToListAsync(cancellationToken);

            if (servers.Count < 1)
                return;

            // First mark them as terminated
            foreach (var server in servers)
            {
                server.Status = ServerStatus.Terminated;
            }

            await database.SaveChangesAsync(cancellationToken);

            // And then use the terminate API (this is done this way to avoid problems if someone just modified the
            // servers in the database)
            bool failures = false;

            foreach (var server in servers)
            {
                logger.LogInformation("Terminating server {Id} as it's been stopped for a while", server.Id);

                try
                {
                    await ec2Controller.TerminateInstance(server.InstanceId);
                }
                catch (Exception e)
                {
                    logger.LogError("Failed to terminate server {Id}: {@E}", server.Id, e);
                    server.Status = ServerStatus.Stopped;
                    failures = true;
                }
            }

            if (failures)
            {
                // We must save this status here as we failed to properly put the earlier saved changes into effect
                // ReSharper disable once MethodSupportsCancellation
                await database.SaveChangesAsync();
            }
        }
    }
}
