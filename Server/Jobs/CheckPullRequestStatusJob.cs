namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;

[DisableConcurrentExecution(60)]
public class CheckPullRequestStatusJob
{
    private readonly ILogger<CheckPullRequestStatusJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;
    private readonly ICLAExemptions claExemptions;
    private readonly bool verboseStatusLogging;

    private readonly bool logPullRequest;

    public CheckPullRequestStatusJob(ILogger<CheckPullRequestStatusJob> logger, NotificationsEnabledDb database,
        IConfiguration configuration, IBackgroundJobClient jobClient, ICLAExemptions claExemptions)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
        this.claExemptions = claExemptions;
        verboseStatusLogging = Convert.ToBoolean(configuration["Github:VerbosePRStatus"]);
        logPullRequest = Convert.ToBoolean(configuration["Github:LogPRCreation"]);
    }

    public async Task Execute(string repository, long pullRequestNumber, string commit, string githubUsername,
        bool open, CancellationToken cancellationToken)
    {
        logger.LogInformation("Update to PR {Repository}/{PullRequestNumber} detected by {GithubUsername}",
            repository, pullRequestNumber, githubUsername);

        var pullRequest =
            await database.GithubPullRequests.FirstOrDefaultAsync(p =>
                p.Repository == repository && p.GithubId == pullRequestNumber, cancellationToken);

        if (pullRequest == null)
        {
            if (logPullRequest)
            {
                await database.LogEntries.AddAsync(
                    new LogEntry($"New Github pull request detected {repository}/{pullRequestNumber}"),
                    cancellationToken);
            }

            pullRequest = new GithubPullRequest
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

                if (verboseStatusLogging && logPullRequest)
                {
                    await database.LogEntries.AddAsync(
                        new LogEntry($"Github pull request open state {repository}/{pullRequestNumber} is now {open}"),
                        cancellationToken);
                }
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
        // Some users (like automated bot accounts) are exempt from requiring CLA signatures
        if (claExemptions.IsExempt(username))
        {
            if (oldStatus is null or false)
                logger.LogInformation("{Username} is exempt from requiring to sign a CLA", username);
            return true;
        }

        if (oldStatus.HasValue)
            return oldStatus.Value;

        var active = await database.Clas.FirstOrDefaultAsync(c => c.Active);

        if (active == null)
        {
            logger.LogWarning("No active CLA");
            return null;
        }

        return await database.ClaSignatures.FirstOrDefaultAsync(s =>
            s.ValidUntil == null && s.ClaId == active.Id && s.GithubAccount == username) != null;
    }
}
