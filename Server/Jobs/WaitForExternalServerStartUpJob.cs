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

    public class WaitForExternalServerStartUpJob
    {
        private readonly ILogger<WaitForExternalServerStartUpJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;
        private readonly IExternalServerSSHAccess sshAccess;

        public WaitForExternalServerStartUpJob(ILogger<WaitForExternalServerStartUpJob> logger,
            NotificationsEnabledDb database, IBackgroundJobClient jobClient, IExternalServerSSHAccess sshAccess)
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
                logger.LogWarning("Server {Id} (external) not found for startup check", id);
                return;
            }

            if (server.Status == ServerStatus.Running || server.Status == ServerStatus.Provisioning)
            {
                logger.LogInformation("External server {Id} is already up, skipping check job", id);
                return;
            }

            if (server.Status == ServerStatus.Stopping &&
                DateTime.UtcNow - server.StatusLastChecked < TimeSpan.FromSeconds(15))
            {
                throw new Exception($"External server {id} has been in stopping status too short time");
            }

            bool up = false;

            try
            {
                sshAccess.ConnectTo(server.PublicAddress.ToString(), server.SSHKeyFileName);
                up = true;
            }
            catch (SocketException)
            {
                logger.LogInformation("Connection failed (socket exception), server is probably not up yet");
            }
            catch (SshOperationTimeoutException)
            {
                logger.LogInformation("Connection failed (ssh timed out), server is probably not up yet");
            }

            if (up)
            {
                server.Status = ServerStatus.Running;
            }

            server.StatusLastChecked = DateTime.UtcNow;
            server.BumpUpdatedAt();
            await database.SaveChangesAsync(cancellationToken);

            if (!up)
            {
                logger.LogTrace("External server {Id} is not up currently", id);
                jobClient.Schedule<ProvisionControlledServerJob>(x => Execute(id, CancellationToken.None),
                    TimeSpan.FromSeconds(30));
            }
            else
            {
                logger.LogInformation("External server {Id} is now up", id);
            }
        }
    }
}
