namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Services;

/// <summary>
///   Watches app-owned PRs for closure, conflicts, and aging pending edits.
/// </summary>
[DisableConcurrentExecution(1000)]
public sealed class MonitorTranslationPullRequestsJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly ITranslationGithubTokenProvider tokenProvider;
    private readonly IBackgroundJobClient jobs;
    private readonly IConfiguration configuration;

    public MonitorTranslationPullRequestsJob(ApplicationDbContext database,
        ITranslationGithubTokenProvider tokenProvider, IBackgroundJobClient jobs,
        IConfiguration configuration)
    {
        this.database = database;
        this.tokenProvider = tokenProvider;
        this.jobs = jobs;
        this.configuration = configuration;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetToken(cancellationToken);
        if (token == null)
            return;

        using var github = new TranslationGithubClient(token);
        var tracked = await database.TranslationPullRequests.Where(pr => pr.Open).ToListAsync(cancellationToken);
        var thresholdHours = Math.Clamp(configuration.GetValue("Translations:OpenPrPendingHours", 3), 1, 24);
        var cutoff = DateTime.UtcNow.AddHours(-thresholdHours);
        foreach (var pr in tracked)
        {
            var project = await database.TranslationProjects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == pr.ProjectId && p.Enabled, cancellationToken);
            if (project?.RepositoryFullName == null)
                continue;

            var remote = await github.GetPullRequest(project.RepositoryFullName, pr.Number,
                cancellationToken);
            pr.LastCheckedAt = DateTime.UtcNow;
            if (remote is not { Open: true })
            {
                pr.Open = false;
                continue;
            }

            // Surface the external branch edit without overwriting it.
            if (remote.HeadSha != pr.HeadSha)
            {
                // TODO: we should probably trigger a comment or some kind of warning that the PR needs merging before
                // new changes are pushed.
                continue;
            }

            var oldPending = await database.TranslationValues.AnyAsync(value =>
                value.ProjectId == project.Id && value.PendingRevision != null &&
                value.PendingSince <= cutoff, cancellationToken);
            if (remote.Mergeable == false || oldPending)
            {
                jobs.Enqueue<PublishTranslationProjectJob>(job =>
                    job.Execute(project.Id, CancellationToken.None));
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
