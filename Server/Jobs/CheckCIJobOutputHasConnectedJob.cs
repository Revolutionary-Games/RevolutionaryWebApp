namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    public class CheckCIJobOutputHasConnectedJob
    {
        private readonly ILogger<CheckCIJobOutputHasConnectedJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public CheckCIJobOutputHasConnectedJob(ILogger<CheckCIJobOutputHasConnectedJob> logger,
            NotificationsEnabledDb database, IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, long ciJobId, long serverId,
            CancellationToken cancellationToken)
        {
            var job = await database.CiJobs.FirstOrDefaultAsync(
                j => j.CiProjectId == ciProjectId && j.CiBuildId == ciBuildId && j.CiJobId == ciJobId,
                cancellationToken);

            if (job == null)
            {
                logger.LogError("Failed to check if a CI job startup is stuck, can't find the job");
                return;
            }

            if (job.State == CIJobState.Finished)
                return;

            var outputSections = await database.CiJobOutputSections.AsQueryable().CountAsync(s =>
                    s.CiProjectId == ciProjectId && s.CiBuildId == ciBuildId && s.CiJobId == ciJobId,
                cancellationToken);

            if (outputSections > 0)
            {
                logger.LogInformation("CI job {CIProjectId}-{CIBuildId}-{CIJobId} has connected output",
                    ciProjectId, ciBuildId, ciJobId);
                return;
            }

            logger.LogWarning("Detected CI job {CIProjectId}-{CIBuildId}-{CIJobId} as stuck starting",
                ciProjectId, ciBuildId, ciJobId);

            if (job.RunningOnServerId != serverId)
            {
                logger.LogError("Wrong RunningOnServerId in job (detected startup is stuck)");
                job.RunningOnServerId = serverId;
                await database.SaveChangesAsync(cancellationToken);
            }

            jobClient.Enqueue<SetFinishedCIJobStatusJob>(x =>
                x.Execute(ciProjectId, ciBuildId, ciJobId, false, CancellationToken.None));
        }
    }
}
