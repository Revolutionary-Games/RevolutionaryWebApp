namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.Extensions.Logging;
    using Models;
    using Renci.SshNet.Common;
    using Services;
    using Shared.Models;

    public class ProvisionControlledServerJob
    {
        // TODO: put this somewhere more sensible
        private const string ProvisioningCommand =
            "sudo dnf install -y podman curl git git-lfs dotnet-runtime-5.0 && " +
            "git lfs install && " +
            "sudo mkdir -p /executor_cache && sudo chown -R centos:centos /executor_cache";

        private readonly ILogger<ProvisionControlledServerJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IEC2Controller ec2Controller;
        private readonly IBackgroundJobClient jobClient;
        private readonly ControlledServerSSHAccess sshAccess;

        public ProvisionControlledServerJob(ILogger<ProvisionControlledServerJob> logger,
            NotificationsEnabledDb database, IEC2Controller ec2Controller, IBackgroundJobClient jobClient,
            ControlledServerSSHAccess sshAccess)
        {
            this.logger = logger;
            this.database = database;
            this.ec2Controller = ec2Controller;
            this.jobClient = jobClient;
            this.sshAccess = sshAccess;
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
                    server.PublicAddress = EC2Controller.InstanceIP(status);
                    server.RunningSince = DateTime.UtcNow;
                    await PerformProvisioningCommands(server);
                    break;
                }
                else
                {
                    logger.LogInformation("Waiting for server {Id} (instance: {InstanceId}) to boot up", id,
                        server.InstanceId);
                    server.StatusLastChecked = DateTime.UtcNow;
                    server.BumpUpdatedAt();
                    await database.SaveChangesAsync(cancellationToken);
                }
            }

            // If not provisioned yet, need to requeue this job
            if (!server.ProvisionedFully)
            {
                logger.LogTrace("Server {Id} not yet fully provisioned", id);
                jobClient.Schedule<ProvisionControlledServerJob>(x => Execute(id, CancellationToken.None),
                    TimeSpan.FromSeconds(10));
            }
        }

        // TODO: make this async
        private async Task PerformProvisioningCommands(ControlledServer server)
        {
            logger.LogInformation("Beginning SSH connect to provision server at: {PublicAddress}",
                server.PublicAddress);

            // TODO: there should probably be a maximum number of times this is attempted
            try
            {
                sshAccess.ConnectTo(server.PublicAddress.ToString());
            }
            catch (SocketException)
            {
                logger.LogWarning("Connection failed (socket exception), server is probably not up yet");
                return;
            }
            catch (SshOperationTimeoutException)
            {
                logger.LogWarning("Connection failed (ssh timed out), server is probably not up yet");
                return;
            }

            logger.LogInformation("Connected, running provisioning command...");

            var start = DateTime.UtcNow;
            var result = sshAccess.RunCommand(ProvisioningCommand);

            if (!result.Success)
            {
                logger.LogWarning("Failed provision result ({ExitCode}: {Result}", result.ExitCode, result.Result);
                throw new Exception($"Provisioning commands failed ({result.ExitCode}): {result.Error}");
            }

            // Now fully provisioned
            server.ProvisionedFully = true;
            server.Status = ServerStatus.Running;
            server.LastMaintenance = DateTime.UtcNow;
            server.StatusLastChecked = DateTime.UtcNow;
            server.ReservationType = ServerReservationType.None;
            server.BumpUpdatedAt();
            await database.SaveChangesAsync();

            var elapsed = DateTime.UtcNow - start;
            logger.LogInformation("Completed provisioning for server {Id}, elapsed: {Elapsed}", server.Id, elapsed);
        }
    }
}
