namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;

/// <summary>
///   Recovers staged actions if scheduling failed or a worker stopped between the two cleanup passes.
/// </summary>
[DisableConcurrentExecution(500)]
public class RecoverPendingModerationJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobs;

    public RecoverPendingModerationJob(ApplicationDbContext database, IBackgroundJobClient jobs)
    {
        this.database = database;
        this.jobs = jobs;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-4);
        var pending = await database.PendingUserModeration.AsNoTracking()
            .Where(action => action.TargetUserId != null && action.CreatedAt <= cutoff &&
                (action.AppliedAt == null || action.CleanedUpAt == null))
            .OrderBy(action => action.CreatedAt).Take(100).ToListAsync(cancellationToken);

        foreach (var action in pending)
        {
            var cleanup = action.AppliedAt != null;
            jobs.Enqueue<ApplyUserModerationJob>(job => job.Execute(action.Id, cleanup, CancellationToken.None));
        }
    }
}
