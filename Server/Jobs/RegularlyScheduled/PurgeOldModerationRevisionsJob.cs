namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;

[DisableConcurrentExecution(2200)]
public class PurgeOldModerationRevisionsJob : IJob
{
    private readonly ApplicationDbContext database;

    public PurgeOldModerationRevisionsJob(ApplicationDbContext database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-ModerationRevision.RetentionMonths);
        await database.ModerationRevisions.Where(revision => revision.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
