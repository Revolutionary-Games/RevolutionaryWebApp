namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;

    [DisableConcurrentExecution(60)]
    public class SetCLAGithubCommitStatusJob
    {
        private readonly ILogger<SetCLAGithubCommitStatusJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IGithubCommitStatusReporter statusReporter;

        public SetCLAGithubCommitStatusJob(ILogger<SetCLAGithubCommitStatusJob> logger, NotificationsEnabledDb database,
            IGithubCommitStatusReporter statusReporter)
        {
            this.logger = logger;
            this.database = database;
            this.statusReporter = statusReporter;
        }

        public async Task Execute(long pullRequestId, CancellationToken cancellationToken)
        {
            var pullRequest =
                await database.GithubPullRequests.FindAsync(new object[] { pullRequestId }, cancellationToken);

            if (pullRequest == null)
            {
                logger.LogError("No pull request with ID {PullRequestId} found to set CLA commit status ",
                    pullRequestId);
                return;
            }

            var status = GithubAPI.CommitStatus.Pending;
            var message = "CLA status unknown";

            if (pullRequest.ClaSigned == true)
            {
                status = GithubAPI.CommitStatus.Success;
                message = "CLA is signed";
            }
            else if (pullRequest.ClaSigned == false)
            {
                status = GithubAPI.CommitStatus.Failure;
                message = "CLA signature missing";
            }

            if (!await statusReporter.SetCommitStatus(pullRequest.Repository, pullRequest.LatestCommit,
                status, statusReporter.CreateStatusUrlForCLA(), message, "CLA"))
            {
                logger.LogError("Failed to set commit status for CLA on PR: {PullRequestId}", pullRequestId);
            }
        }
    }
}
