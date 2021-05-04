namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    public class RunJobOnServerJob
    {
        private readonly ILogger<RunJobOnServerJob> logger;
        private readonly NotificationsEnabledDb database;

        public RunJobOnServerJob(ILogger<RunJobOnServerJob> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, long ciJobId, long serverId,
            CancellationToken cancellationToken)
        {
            var job = await database.CiJobs.FindAsync(ciProjectId, ciBuildId, ciJobId, cancellationToken);
            var server = await database.ControlledServers.FindAsync(serverId, cancellationToken);

            if (server == null)
                throw new ArgumentException("Could not find server to run build on");

            if (job == null)
            {
                ReleaseServerReservation(server);
                logger.LogWarning("Skipping CI job as it doesn't exist");
                return;
            }

            // TODO: check if ciJobId matches the reservation on the server?

            logger.LogInformation("Pretending that CI job {CIProjectId}-{CIBuildId}-{CIJobId} has run correctly",
                ciProjectId, ciBuildId, ciJobId);

            ReleaseServerReservation(server);

            // After running the job, the changes saving should now be skipped
            // ReSharper disable once MethodSupportsCancellation
            await database.SaveChangesAsync();

            // TODO: queue a job to check the ciBuild overall status (if needed)
        }

        private void ReleaseServerReservation(ControlledServer server)
        {
            server.ReservationType = ServerReservationType.None;
            server.ReservedFor = null;
        }
    }
}
