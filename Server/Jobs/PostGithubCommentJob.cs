namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using Services;

[DisableConcurrentExecution(30)]
public class PostGithubCommentJob
{
    private readonly ILogger<PostGithubCommentJob> logger;
    private readonly IGithubCommitStatusReporter githubAPI;

    // TODO: could maybe use own tokens instead of always the one also used for commit status reporting
    public PostGithubCommentJob(ILogger<PostGithubCommentJob> logger, IGithubCommitStatusReporter githubAPI)
    {
        this.logger = logger;
        this.githubAPI = githubAPI;
    }

    public async Task Execute(string qualifiedRepositoryName, long issueOrPullRequest, string comment,
        CancellationToken cancellationToken)
    {
        if (!await githubAPI.PostComment(qualifiedRepositoryName, issueOrPullRequest, comment))
        {
            logger.LogError("Failed to post comment to Github");

            // In case we are hitting a rate limit, wait a bit here
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            throw new Exception("Failed to post comment to Github");
        }
    }
}
