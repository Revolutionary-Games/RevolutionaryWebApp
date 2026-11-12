namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;
using Services;

/// <summary>
///   Checks recent translations daily, including imported text and translations whose source changed.
/// </summary>
[DisableConcurrentExecution(1800)]
public class CheckTranslationQualityJob : IJob
{
    private readonly NotificationsEnabledDb database;

    public CheckTranslationQualityJob(NotificationsEnabledDb database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var projectIds = await database.TranslationProjects.AsNoTracking().Where(project => project.Enabled)
            .Select(project => project.Id).OrderBy(id => id).ToListAsync(cancellationToken);
        foreach (var projectId in projectIds)
            await ExecuteForProject(projectId, false, cancellationToken);
    }

    public async Task ExecuteForProject(long projectId, bool allTranslations, CancellationToken cancellationToken)
    {
        await using var projectLock = await TranslationProjectAdvisoryLock.AcquireForRepositorySyncAsync(database, projectId,
            cancellationToken);
        var project = await database.TranslationProjects.FirstOrDefaultAsync(item => item.Id == projectId &&
            item.Enabled, cancellationToken);
        if (project == null)
            return;

        await TranslationQualityChecks.CheckProject(database, project,
            allTranslations ? null : DateTime.UtcNow.AddHours(-72), cancellationToken);
        database.ChangeTracker.Clear();
    }
}
