namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    public class CheckOverallBuildStatusJob
    {
        private readonly ILogger<CheckOverallBuildStatusJob> logger;
        private readonly NotificationsEnabledDb database;

        public CheckOverallBuildStatusJob(ILogger<CheckOverallBuildStatusJob> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, CancellationToken cancellationToken)
        {
            var build = await database.CiBuilds.Include(b => b.CiJobs)
                .FirstOrDefaultAsync(b => b.CiProjectId == ciProjectId && b.CiBuildId == ciBuildId,
                    cancellationToken: cancellationToken);

            if (build == null)
            {
                logger.LogError("Failed to get CI build to check overall status on");
                return;
            }

            // If the status has already been set, ignore
            switch (build.Status)
            {
                case BuildStatus.Succeeded:
                case BuildStatus.Failed:
                    return;
            }

            var shouldBeStatus = BuildStatus.Succeeded;
            int failedBuilds = 0;

            foreach (var job in build.CiJobs)
            {
                if (job.State != CIJobState.Finished)
                {
                    if (shouldBeStatus == BuildStatus.Succeeded)
                        shouldBeStatus = BuildStatus.Running;
                }
                else if (!job.Succeeded)
                {
                    ++failedBuilds;
                }
            }

            if (failedBuilds >= build.CiJobs.Count)
            {
                shouldBeStatus = BuildStatus.Failed;
            }
            else if (failedBuilds > 0)
            {
                shouldBeStatus = BuildStatus.GoingToFail;
            }

            if (build.Status == shouldBeStatus)
                return;

            build.Status = shouldBeStatus;
            await database.SaveChangesAsync(cancellationToken);

            // Don't send notifications yet if we only know that the build is going to fail, but not all jobs
            // are complete yet
            if (build.Status == BuildStatus.Running || build.Status == BuildStatus.GoingToFail)
                return;

            // TODO: send emails on failure
        }
    }
}
