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

    public class SetFinishedCIJobStatusJob : BaseCIJobManagingJob
    {
        private readonly ILogger<SetFinishedCIJobStatusJob> logger;
        private readonly NotificationsEnabledDb database;

        public SetFinishedCIJobStatusJob(ILogger<SetFinishedCIJobStatusJob> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient, GithubCommitStatusReporter statusReporter) : base(logger, database,
            jobClient, statusReporter)
        {
            this.logger = logger;
            this.database = database;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, long ciJobId, bool success,
            CancellationToken cancellationToken)
        {
            var job = await database.CiJobs.Include(j => j.Build).ThenInclude(b => b.CiProject)
                .FirstOrDefaultAsync(
                    j => j.CiProjectId == ciProjectId && j.CiBuildId == ciBuildId && j.CiJobId == ciJobId,
                    cancellationToken);

            if (job == null)
            {
                logger.LogError("Failed to get CI job to report final status on");
                return;
            }

            logger.LogInformation("CI job {CIProjectId}-{CIBuildId}-{CIJobId} is now finished with status: {Success}",
                ciProjectId, ciBuildId, ciJobId, success);

            job.Succeeded = success;
            job.State = CIJobState.Finished;
            job.FinishedAt = DateTime.UtcNow;
            job.BuildOutputConnectKey = null;

            // Release the server reservation and send notifications about the job
            var server =
                await Database.ControlledServers.FindAsync(new object[] { job.RunningOnServerId }, cancellationToken);


            job.RunningOnServerId = -1;

            if (server == null)
                throw new ArgumentException("Could not find server to release now that a build is complete");

            await OnJobEnded(server, job);
        }
    }
}
