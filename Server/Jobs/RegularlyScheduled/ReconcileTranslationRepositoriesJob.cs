namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;

/// <summary>
///   Rechecks public repositories so missed webhook deliveries do not strand imports.
/// </summary>
[DisableConcurrentExecution(1800)]
public sealed class ReconcileTranslationRepositoriesJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobs;

    public ReconcileTranslationRepositoriesJob(ApplicationDbContext database, IBackgroundJobClient jobs)
    {
        this.database = database;
        this.jobs = jobs;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var projects = await database.TranslationProjects.AsNoTracking()
            .Where(project => project.Enabled && project.RepositoryFullName != null)
            .Select(project => project.Id).ToListAsync(cancellationToken);
        foreach (var projectId in projects)
        {
            jobs.Enqueue<ImportTranslationProjectJob>(job => job.Execute(projectId, CancellationToken.None));
        }
    }
}
