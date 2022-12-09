namespace ThriveDevCenter.Server.Jobs;

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models.Enums;

public class InvalidatePullRequestsWithCLASignaturesJob : IJob
{
    private readonly ILogger<InvalidatePullRequestsWithCLASignaturesJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public InvalidatePullRequestsWithCLASignaturesJob(ILogger<InvalidatePullRequestsWithCLASignaturesJob> logger,
        NotificationsEnabledDb database, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var pullRequests = await database.GithubPullRequests.Include(p => p.AutoComments)
            .Where(pr => pr.ClaSigned == true)
            .ToListAsync(cancellationToken);

        var comments = await database.GithubAutoComments.Where(c =>
            c.Enabled && c.Condition == AutoCommentCondition.IfCLABecomesInvalid).ToListAsync(cancellationToken);

        foreach (var pullRequest in pullRequests)
        {
            logger.LogInformation("Unmarking PR {Repository}/{GithubId} as having CLA signed",
                pullRequest.Repository, pullRequest.GithubId);
            pullRequest.ClaSigned = null;
        }

        await database.SaveChangesAsync(cancellationToken);

        // Open PRs need immediately (this way new webhook shouldn't be able to get their new data lost by this)
        // to be checked if they are still good
        foreach (var pullRequest in pullRequests)
        {
            if (pullRequest.Open)
            {
                // TODO: make separate job that doesn't update PR data other than the signature status
                jobClient.Enqueue<CheckPullRequestStatusJob>(x => x.Execute(pullRequest.Repository,
                    pullRequest.GithubId, pullRequest.LatestCommit, pullRequest.AuthorUsername, pullRequest.Open,
                    CancellationToken.None));
            }
        }

        if (comments.Count > 0)
            await PostComments(pullRequests, comments);
    }

    private async Task PostComments(List<GithubPullRequest> pullRequests,
        List<GithubAutoComment> githubAutoComments)
    {
        // TODO: it would be better to only send the comment if the PR won't become properly signed after
        // CheckPullRequestStatusJob runs very shortly
        foreach (var pullRequest in pullRequests)
        {
            // Don't comment on PRs that are closed
            if (!pullRequest.Open)
                continue;

            foreach (var comment in githubAutoComments)
            {
                if (string.IsNullOrEmpty(comment.Repository) || comment.Repository == "*" ||
                    comment.Repository == pullRequest.Repository)
                {
                    jobClient.Enqueue<PostGithubCommentJob>(x =>
                        x.Execute(pullRequest.Repository, pullRequest.GithubId, comment.CommentText,
                            CancellationToken.None));
                    pullRequest.AutoComments.Add(comment);

                    break;
                }
            }
        }

        await database.SaveChangesAsync();
    }
}
