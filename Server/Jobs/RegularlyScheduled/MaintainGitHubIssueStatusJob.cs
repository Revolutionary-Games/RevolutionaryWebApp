namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.Webhook;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Services;

public class MaintainGitHubIssueStatusJob : IJob
{
    private readonly ILogger<MaintainGitHubIssueStatusJob> logger;
    private readonly IConfiguration configuration;
    private readonly IGithubCommitStatusReporter githubApi;

    public MaintainGitHubIssueStatusJob(ILogger<MaintainGitHubIssueStatusJob> logger, IConfiguration configuration,
        IGithubCommitStatusReporter githubApi)
    {
        this.logger = logger;
        this.configuration = configuration;
        this.githubApi = githubApi;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var reposToProcess = configuration.GetSection("Github:ReposToMaintainStatus").Get<string>()?.Split(',');

        if (reposToProcess == null || reposToProcess.Length == 0)
        {
            logger.LogInformation("No repositories configured for MaintainGitHubIssueStatusJob");
            return;
        }

        var webhookUrl = configuration["Discord:MaintainStatusWebhookUrl"];
        if (string.IsNullOrEmpty(webhookUrl))
        {
            logger.LogError("Discord:MaintainStatusWebhookUrl is not configured");
            return;
        }

        foreach (var repo in reposToProcess)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            List<GithubIssue> issues;
            try
            {
                issues = await githubApi.GetIssues(repo);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to fetch issues for repo {Repo}", repo);
                continue;
            }

            // Filter out pull requests and keep only issues with 'triage' label or no labels
            var untriagedIssues = issues
                .Where(i => i.PullRequest == null)
                .Where(i => i.Labels.Count == 0 ||
                    i.Labels.Any(l => l.Name.Equals("triage", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (untriagedIssues.Count > 0)
            {
                var firstIssue = untriagedIssues[0];
                var message = $"There are {untriagedIssues.Count} untriaged issues in {repo} to triage. " +
                    $"The first one is: {firstIssue.HtmlUrl}\n" +
                    "Any team members who have time, please check them. Comment here if someone takes care of it.\n " +
                    "Remember to label any issues with just one report as 'more-reports-wanted'";

                try
                {
                    using var client = new DiscordWebhookClient(webhookUrl);
                    await SendDiscordWebhookMessageJob.SendDiscordMessageInChunks(message, client, logger,
                        cancellationToken);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Failed to send Discord webhook message for repo {Repo}", repo);
                }
            }
        }
    }
}
