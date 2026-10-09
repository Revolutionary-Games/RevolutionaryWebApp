namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;

/// <summary>
///   Queues Thursday and Friday afternoon publishes for changes at least a day old.
/// </summary>
[DisableConcurrentExecution(500)]
public sealed class ScheduleTranslationPublishesJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobs;

    public ScheduleTranslationPublishesJob(ApplicationDbContext database, IBackgroundJobClient jobs)
    {
        this.database = database;
        this.jobs = jobs;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now.DayOfWeek is not (DayOfWeek.Thursday or DayOfWeek.Friday) || now.Hour < 12)
            return;

        var cutoff = now.AddDays(-1);
        var busyAfter = now.AddHours(-2);
        var projects = await database.TranslationValues.AsNoTracking()
            .Where(value => value.PendingRevision != null && value.PendingSince <= cutoff)
            .Select(value => value.ProjectId).Distinct().ToListAsync(cancellationToken);
        foreach (var projectId in projects)
        {
            if (!await database.TranslationProjects.AnyAsync(project => project.Id == projectId &&
                    project.Enabled && project.RepositoryFullName != null, cancellationToken))
            {
                continue;
            }

            if (await database.TranslationSyncRuns.AnyAsync(run => run.ProjectId == projectId &&
                        run.Type == "Publish" && run.Status == "Running" && run.StartedAt >= busyAfter,
                    cancellationToken))
            {
                continue;
            }

            jobs.Enqueue<PublishTranslationProjectJob>(job => job.Execute(projectId, CancellationToken.None));
        }
    }
}
