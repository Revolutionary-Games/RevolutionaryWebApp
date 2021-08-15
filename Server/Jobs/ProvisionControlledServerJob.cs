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

    public class ProvisionControlledServerJob : BaseProvisionServerJob
    {
        private const string ProvisioningCommand = GeneralProvisionCommandPart +
            " && sudo chown -R centos:centos /executor_cache";
        
        private readonly IEC2Controller ec2Controller;
        private readonly IBackgroundJobClient jobClient;
        private readonly ControlledServerSSHAccess sshAccess;

        public ProvisionControlledServerJob(ILogger<ProvisionControlledServerJob> logger,
            NotificationsEnabledDb database, IEC2Controller ec2Controller, IBackgroundJobClient jobClient,
            ControlledServerSSHAccess sshAccess) : base(logger, database)
        {
            this.ec2Controller = ec2Controller;
            this.jobClient = jobClient;
            this.sshAccess = sshAccess;
        }

        public async Task Execute(long id, CancellationToken cancellationToken)
        {
            var server = await Database.ControlledServers.FindAsync(id);

            if (CheckServerDataIsFineForProvisioning(id, server))
                return;

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
                    server.PublicAddress = EC2Controller.InstanceIP(status);
                    server.RunningSince = DateTime.UtcNow;
                    await PerformProvisioningCommands(server, ProvisioningCommand);
                }
                else
                {
                    Logger.LogInformation("Waiting for server {Id} (instance: {InstanceId}) to boot up", id,
                        server.InstanceId);
                    server.StatusLastChecked = DateTime.UtcNow;
                    server.BumpUpdatedAt();
                    await Database.SaveChangesAsync(cancellationToken);
                }
                
                break;
            }

            // If not provisioned yet, need to requeue this job
            if (!server.ProvisionedFully)
            {
                Logger.LogTrace("Server {Id} not yet fully provisioned", id);
                jobClient.Schedule<ProvisionControlledServerJob>(x => Execute(id, CancellationToken.None),
                    TimeSpan.FromSeconds(10));
            }
        }

        protected override BaseSSHAccess ConnectWithSSH(BaseServer server)
        {
            sshAccess.ConnectTo(server.PublicAddress.ToString());
            return sshAccess;
        }
    }
}
