namespace RevolutionaryWebApp.Server.Jobs;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

public class CheckPullRequestsAfterNewSignatureJob
{
    private readonly ILogger<CheckPullRequestsAfterNewSignatureJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public CheckPullRequestsAfterNewSignatureJob(ILogger<CheckPullRequestsAfterNewSignatureJob> logger,
        NotificationsEnabledDb database, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(string githubUsername, CancellationToken cancellationToken)
    {
        var pullRequests = await database.GithubPullRequests
            .Where(p => p.ClaSigned != true && p.AuthorUsername == githubUsername).ToListAsync(cancellationToken);

        foreach (var pullRequest in pullRequests)
        {
            pullRequest.ClaSigned = null;
        }

        await database.SaveChangesAsync(cancellationToken);

        foreach (var pullRequest in pullRequests)
        {
            // Don't need to run this for closed ones as the open event will cause a re-check anyway
            if (!pullRequest.Open)
                continue;

            logger.LogInformation(
                "New CLA signature made that matches pull request: {Repository}/{GithubId} by {GithubUsername}",
                pullRequest.Repository, pullRequest.GithubId, githubUsername);

            // This shouldn't cause any problems regarding data consistency but:
            // TODO: make separate job that doesn't update PR data other than the signature status
            jobClient.Enqueue<CheckPullRequestStatusJob>(x => x.Execute(pullRequest.Repository,
                pullRequest.GithubId, pullRequest.LatestCommit, pullRequest.AuthorUsername, pullRequest.Open,
                CancellationToken.None));
        }
    }
}
