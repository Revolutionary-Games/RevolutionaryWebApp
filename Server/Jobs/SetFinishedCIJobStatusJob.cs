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

            if (job.RunningOnServerId == -1)
            {
                logger.LogError("CI job doesn't have RunningOnServerId set for SetFinishedCIJobStatus");

                if (job.State != CIJobState.Finished)
                {
                    logger.LogError("Forcing job state to be a failure");
                    job.SetFinishSuccess(false);
                    await Database.SaveChangesAsync(cancellationToken);
                }

                // Just for safety make sure that the job doesn't get stuck in going to fail status
                JobClient.Enqueue<CheckOverallBuildStatusJob>(x =>
                    x.Execute(job.CiProjectId, job.CiBuildId, CancellationToken.None));
                return;
            }

            logger.LogInformation("CI job {CIProjectId}-{CIBuildId}-{CIJobId} is now finished with status: {Success}",
                ciProjectId, ciBuildId, ciJobId, success);

            job.SetFinishSuccess(success);

            // Release the server reservation and send notifications about the job
            var server =
                await Database.ControlledServers.FindAsync(new object[] { job.RunningOnServerId }, cancellationToken);

            if (server == null)
                throw new ArgumentException("Could not find server to release now that a build is complete");

            await OnJobEnded(server, job);
        }
    }
}
