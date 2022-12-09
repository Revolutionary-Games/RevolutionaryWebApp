namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models.Enums;

[DisableConcurrentExecution(60)]
public class CheckAutoCommentsToPostJob
{
    private readonly ILogger<CheckAutoCommentsToPostJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public CheckAutoCommentsToPostJob(ILogger<CheckAutoCommentsToPostJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(long pullRequestId, CancellationToken cancellationToken)
    {
        var pullRequest = await database.GithubPullRequests.Include(p => p.AutoComments)
            .FirstOrDefaultAsync(p => p.Id == pullRequestId, cancellationToken);

        if (pullRequest == null)
        {
            logger.LogError("No pull request with ID {PullRequestId} found to post comments on", pullRequestId);
            return;
        }

        var possibleComments = await database.GithubAutoComments.Where(c =>
            c.Enabled && (string.IsNullOrEmpty(c.Repository) || c.Repository == "*" ||
                c.Repository == pullRequest.Repository)).ToListAsync(cancellationToken);

        foreach (var possibleComment in possibleComments)
        {
            bool post = false;

            switch (possibleComment.Condition)
            {
                case AutoCommentCondition.Always:
                    post = true;
                    break;
                case AutoCommentCondition.OnceOnPullRequest:
                    // Contains doesn't work here. Has to be an ID check like this
                    post = pullRequest.AutoComments.All(c => c.Id != possibleComment.Id);
                    break;
                case AutoCommentCondition.IfCLANotSigned:
                    post = pullRequest.AutoComments.All(c => c.Id != possibleComment.Id) &&
                        pullRequest.ClaSigned == false;
                    break;
                case AutoCommentCondition.IfCLABecomesInvalid:
                    // Handled elsewhere (InvalidatePullRequestsWithCLASignaturesJob)
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            if (!post)
                continue;

            jobClient.Enqueue<PostGithubCommentJob>(x =>
                x.Execute(pullRequest.Repository, pullRequest.GithubId, possibleComment.CommentText,
                    CancellationToken.None));
            pullRequest.AutoComments.Add(possibleComment);
        }

        // Not cancellable here as we have potentially already queued up the comment posts so we don't want to lose
        // that information
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();
    }
}
