namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Utilities;

    public class CheckAndStartCIBuild
    {
        private readonly ILogger<CheckAndStartCIBuild> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;
        private readonly ILocalTempFileLocks localTempFileLocks;
        private readonly GithubCommitStatusReporter statusReporter;

        public CheckAndStartCIBuild(ILogger<CheckAndStartCIBuild> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient, ILocalTempFileLocks localTempFileLocks,
            GithubCommitStatusReporter statusReporter)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
            this.localTempFileLocks = localTempFileLocks;
            this.statusReporter = statusReporter;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, CancellationToken cancellationToken)
        {
            var build = await database.CiBuilds.Include(c => c.CiProject)
                .FirstOrDefaultAsync(c => c.CiProjectId == ciProjectId && c.CiBuildId == ciBuildId, cancellationToken);

            if (build == null)
            {
                logger.LogError("Failed to find CIBuild to start");
                return;
            }

            // Update our local repo copy and see what the wanted build config is
            var semaphore = localTempFileLocks.GetTempFilePath($"ciRepos/{ciProjectId}", out string tempPath);

            await semaphore.WaitAsync(cancellationToken);

            try
            {
                await GitRunHelpers.EnsureRepoIsCloned(build.CiProject.RepositoryCloneUrl, tempPath, cancellationToken);

                // TODO: force checkout the build ref and clean non-tracked files and changes to get the build file tree

                // TODO: implement reading the build configuration file
            }
            finally
            {
                semaphore.Release();
            }

            // Then queue the jobs we found
            // TODO: for now, for testing purposes just create one testing job

            var job = new CiJob()
            {
                CiProjectId = ciProjectId,
                CiBuildId = ciBuildId,

                // TODO: assign sequential ids when multiple jobs are added
                CiJobId = 1,
                JobName = "test"
            };

            await database.CiJobs.AddAsync(job, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);

            // Send status to github
            if (!await statusReporter.SetCommitStatus(build.CiProject.RepositoryFullName, build.CommitHash,
                GithubAPI.CommitStatus.Pending, statusReporter.CreateStatusUrlForJob(job), "CI checks starting",
                job.JobName))
            {
                logger.LogError("Failed to set commit status for build's job: {JobName}", job.JobName);
            }

            // Queue remote executor check task which will allocate a server to run the job on
            jobClient.Enqueue<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None));
        }
    }
}
