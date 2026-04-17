namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Periodically deletes pending signups older than 3 days so emails aren't reserved indefinitely.
/// </summary>
public class DeleteOldPendingSignupsJob : IJob
{
    private readonly ILogger<DeleteOldPendingSignupsJob> logger;
    private readonly ApplicationDbContext database;

    public DeleteOldPendingSignupsJob(ILogger<DeleteOldPendingSignupsJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-3);

        var old = await database.PendingUserSignups.Where(p => p.CreatedUtc < cutoff).ToListAsync(cancellationToken);

        if (old.Count == 0)
            return;

        database.PendingUserSignups.RemoveRange(old);
        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Deleted {Count} old pending signups", old.Count);
    }
}
