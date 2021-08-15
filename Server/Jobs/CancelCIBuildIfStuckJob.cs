namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Models;

    public class CancelCIBuildIfStuckJob
    {
        private readonly ILogger<CancelCIBuildIfStuckJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;
        private readonly IEC2Controller ec2Controller;
        private readonly IExternalServerSSHAccess externalServerSSHAccess;

        public CancelCIBuildIfStuckJob(ILogger<CancelCIBuildIfStuckJob> logger,
            NotificationsEnabledDb database, IBackgroundJobClient jobClient, IEC2Controller ec2Controller,
            IExternalServerSSHAccess externalServerSSHAccess)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
            this.ec2Controller = ec2Controller;
            this.externalServerSSHAccess = externalServerSSHAccess;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, long ciJobId, long serverId, bool externalServer,
            CancellationToken cancellationToken)
        {
            var job = await database.CiJobs.FirstOrDefaultAsync(
                j => j.CiProjectId == ciProjectId && j.CiBuildId == ciBuildId && j.CiJobId == ciJobId,
                cancellationToken);

            if (job == null)
            {
                logger.LogError("Failed to check if a CI job is stuck, can't find the job");
                return;
            }

            if (job.State == CIJobState.Finished)
                return;

            logger.LogWarning(
                "Detected CI job {CIProjectId}-{CIBuildId}-{CIJobId} as stuck running (total build time limit reached)",
                ciProjectId, ciBuildId, ciJobId);

            if (externalServer)
            {
                var server =
                    await database.ExternalServers.FindAsync(new object[] { serverId }, cancellationToken);

                if (server == null)
                    throw new ArgumentException("Could not find server to release for a stuck build");

                cancellationToken.ThrowIfCancellationRequested();

                externalServerSSHAccess.ConnectTo(server.PublicAddress.ToString(), server.SSHKeyFileName);
                externalServerSSHAccess.Reboot();

                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message = $"External server {server.Id} timed out running CI job, force rebooting it",
                }, cancellationToken);

                server.StatusLastChecked = DateTime.UtcNow;
                server.ReservationType = ServerReservationType.None;
                server.Status = ServerStatus.Stopping;
                server.BumpUpdatedAt();

                jobClient.Schedule<WaitForExternalServerStartUpJob>(x => x.Execute(server.Id, CancellationToken.None),
                    TimeSpan.FromSeconds(20));
                logger.LogInformation("Successfully commanded reboot on: {ServerId}", server.Id);
            }
            else
            {
                var server =
                    await database.ControlledServers.FindAsync(new object[] { serverId }, cancellationToken);

                if (server == null)
                    throw new ArgumentException("Could not find server to release for a stuck build");

                cancellationToken.ThrowIfCancellationRequested();

                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message =
                        $"Server {server.Id} ({server.InstanceId}) timed out running CI job, stopping it, running " +
                        $"since {server.UpdatedAt}"
                }, cancellationToken);

                await ec2Controller.StopInstance(server.InstanceId, false);
                server.Status = ServerStatus.Stopping;

                if (server.RunningSince != null)
                    server.TotalRuntime += (DateTime.UtcNow - server.RunningSince.Value).TotalSeconds;
                server.RunningSince = null;

                logger.LogInformation("Successfully signaled stop on: {InstanceId}", server.InstanceId);
            }

            if (job.RunningOnServerId != serverId)
            {
                logger.LogError("Wrong RunningOnServerId in job (total runtime limit exceeded)");
                job.RunningOnServerId = serverId;
            }

            // Not cancellable done as the state to terminated is very important to save
            // ReSharper disable once MethodSupportsCancellation
            await database.SaveChangesAsync();

            jobClient.Enqueue<SetFinishedCIJobStatusJob>(x =>
                x.Execute(ciProjectId, ciBuildId, ciJobId, false, CancellationToken.None));
        }
    }
}
