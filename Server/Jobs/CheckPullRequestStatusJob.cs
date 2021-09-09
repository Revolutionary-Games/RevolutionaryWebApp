namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    [DisableConcurrentExecution(60)]
    public class CheckPullRequestStatusJob
    {
        private readonly ILogger<CheckPullRequestStatusJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public CheckPullRequestStatusJob(ILogger<CheckPullRequestStatusJob> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        public async Task Execute(string repository, long pullRequestNumber, string commit, string githubUsername,
            bool open, CancellationToken cancellationToken)
        {
            var pullRequest =
                await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(database.GithubPullRequests, p =>
                    p.Repository == repository && p.GithubId == pullRequestNumber, cancellationToken);

            if (pullRequest == null)
            {
                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message = $"New Github pull request detected {repository}/{pullRequestNumber}",
                }, cancellationToken);

                pullRequest = new GithubPullRequest()
                {
                    Repository = repository,
                    GithubId = pullRequestNumber,
                    LatestCommit = commit,
                    AuthorUsername = githubUsername,
                    Open = open,
                    ClaSigned = await CheckNewCLASignedStatus(null, githubUsername),
                };

                await database.GithubPullRequests.AddAsync(pullRequest, cancellationToken);
            }
            else
            {
                pullRequest.LatestCommit = commit;

                if (pullRequest.Open != open)
                {
                    pullRequest.Open = open;

                    await database.LogEntries.AddAsync(new LogEntry()
                    {
                        Message = $"Github pull request open state {repository}/{pullRequestNumber} is now {open}",
                    }, cancellationToken);
                }

                pullRequest.ClaSigned =
                    await CheckNewCLASignedStatus(pullRequest.ClaSigned, pullRequest.AuthorUsername);

                pullRequest.BumpUpdatedAt();
            }

            await database.SaveChangesAsync(cancellationToken);

            jobClient.Enqueue<CheckAutoCommentsToPostJob>(x => x.Execute(pullRequest.Id, CancellationToken.None));
            jobClient.Enqueue<SetCLAGithubCommitStatusJob>(x => x.Execute(pullRequest.Id, CancellationToken.None));
        }

        private async Task<bool?> CheckNewCLASignedStatus(bool? oldStatus, string username)
        {
            if (oldStatus.HasValue)
                return oldStatus.Value;

            var active = await database.Clas.AsQueryable().FirstOrDefaultAsync(c => c.Active);

            if (active == null)
            {
                logger.LogWarning("No active CLA");
                return null;
            }

            return await database.ClaSignatures.AsQueryable().FirstOrDefaultAsync(s =>
                s.ValidUntil == null && s.ClaId == active.Id && s.GithubAccount == username) != null;
        }
    }
}
