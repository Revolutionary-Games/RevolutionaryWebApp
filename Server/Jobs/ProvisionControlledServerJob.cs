namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Models;

    public class ProvisionControlledServerJob
    {
        private readonly ILogger<ProvisionControlledServerJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly EC2Controller ec2Controller;
        private readonly IBackgroundJobClient jobClient;

        public ProvisionControlledServerJob(ILogger<ProvisionControlledServerJob> logger,
            NotificationsEnabledDb database, EC2Controller ec2Controller, IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.ec2Controller = ec2Controller;
            this.jobClient = jobClient;
        }

        public async Task Execute(long id, CancellationToken cancellationToken)
        {
            var server = await database.ControlledServers.FindAsync(id);

            if (server == null)
            {
                logger.LogWarning("Server {Id} not found for provisioning", id);
                return;
            }

            if (server.ProvisionedFully)
                return;

            if (server.Status != ServerStatus.Provisioning)
                throw new Exception("Server status is not Provisioning for the provisioning job");

            // Check that the server is running before starting provisioning
            foreach (var status in await ec2Controller.GetInstanceStatuses(new List<string>() { server.InstanceId },
                cancellationToken))
            {
                if (status.InstanceId != server.InstanceId)
                    continue;

                var ourStatus = EC2Controller.InstanceStateToStatus(status);

                if (ourStatus == ServerStatus.Running)
                {
                    // We can now perform the provisioning
                    server.RunningSince = DateTime.UtcNow;
                    await PerformProvisioningCommands(server);
                    break;
                }
                else
                {
                    logger.LogInformation("Waiting server {Id} (instance: {InstanceId}) to boot up", id,
                        server.InstanceId);
                }
            }

            server.StatusLastChecked = DateTime.UtcNow;
            server.BumpUpdatedAt();

            // If not provisioned yet, need to requeue this job
            if (!server.ProvisionedFully)
            {
                logger.LogInformation("Server {Id} not yet fully provisioned", id);
                jobClient.Schedule<ProvisionControlledServerJob>(x => Execute(id, CancellationToken.None),
                    TimeSpan.FromSeconds(5));
            }
        }

        private async Task PerformProvisioningCommands(ControlledServer server)
        {
            throw new Exception("TODO: connect with SSH to run stuff");

            // Now fully provisioned
            server.ProvisionedFully = true;
            server.Status = ServerStatus.Running;
            server.LastMaintenance = DateTime.UtcNow;
            logger.LogInformation("Completed provisioning for server {Id}", server.Id);
        }
    }
}
