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

            BuildStatus shouldBeStatus;
            int failedBuilds = 0;

            bool running = false;

            foreach (var job in build.CiJobs)
            {
                if (job.State != CIJobState.Finished)
                {
                    running = true;
                }
                else if (!job.Succeeded)
                {
                    ++failedBuilds;
                }
            }

            if (failedBuilds > 0)
            {
                shouldBeStatus = running ? BuildStatus.GoingToFail : BuildStatus.Failed;
            }
            else if (running)
            {
                shouldBeStatus = BuildStatus.Running;
            }
            else
            {
                shouldBeStatus = BuildStatus.Succeeded;
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
