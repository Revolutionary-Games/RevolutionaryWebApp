namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.Extensions.Logging;
    using Models;
    using Renci.SshNet.Common;
    using Services;
    using Shared.Models;

    public class ProvisionExternalServerJob
    {
        private readonly ILogger<ProvisionExternalServerJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;
        private readonly ExternalServerSSHAccess sshAccess;

        public ProvisionExternalServerJob(ILogger<ProvisionExternalServerJob> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient,
            ExternalServerSSHAccess sshAccess)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
            this.sshAccess = sshAccess;
        }

        public async Task Execute(long id, CancellationToken cancellationToken)
        {
            var server = await database.ExternalServers.FindAsync(id);

            if (server == null)
            {
                logger.LogWarning("Server {Id} (external) not found for provisioning", id);
                return;
            }

            if (server.ProvisionedFully)
                return;

            if (server.Status != ServerStatus.Provisioning)
                throw new Exception("Server status is not Provisioning for the provisioning job");

            PerformProvisioningCommands(server);

            server.StatusLastChecked = DateTime.UtcNow;
            server.BumpUpdatedAt();
            await database.SaveChangesAsync(cancellationToken);

            // If not provisioned yet, need to requeue this job
            if (!server.ProvisionedFully)
            {
                logger.LogTrace("Server {Id} not yet fully provisioned", id);
                jobClient.Schedule<ProvisionControlledServerJob>(x => Execute(id, CancellationToken.None),
                    TimeSpan.FromSeconds(10));
            }
        }

        // TODO: make this async
        private void PerformProvisioningCommands(ExternalServer server)
        {
            logger.LogInformation("Beginning SSH connect to provision external server at: {PublicAddress}",
                server.PublicAddress);

            // TODO: there should probably be a maximum number of times this is attempted
            try
            {
                sshAccess.ConnectTo(server.PublicAddress.ToString(), server.SSHKeyFileName);
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
            var result = sshAccess.RunCommand(ProvisionControlledServerJob.GeneralProvisionCommandPart);

            if (!result.Success)
            {
                logger.LogWarning("Failed provision result ({ExitCode}: {Result}", result.ExitCode, result.Result);
                throw new Exception($"Provisioning commands failed ({result.ExitCode}): {result.Error}");
            }

            // Now fully provisioned
            server.ProvisionedFully = true;
            server.Status = ServerStatus.Running;
            server.LastMaintenance = DateTime.UtcNow;
            server.WantsMaintenance = false;
            server.ReservationType = ServerReservationType.None;

            var elapsed = DateTime.UtcNow - start;
            logger.LogInformation("Completed provisioning for external server {Id}, elapsed: {Elapsed}", server.Id,
                elapsed);
        }
    }
}
