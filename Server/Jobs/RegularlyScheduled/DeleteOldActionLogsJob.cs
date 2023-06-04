namespace ThriveDevCenter.Server.Jobs.RegularlyScheduled;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

public class DeleteOldActionLogsJob : IJob
{
    private readonly ILogger<DeleteOldActionLogsJob> logger;
    private readonly ApplicationDbContext database;

    public DeleteOldActionLogsJob(ILogger<DeleteOldActionLogsJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var deleteCutoff = DateTime.UtcNow - AppInfo.DeleteActionLogsAfter;

        // See the comment in SessionCleanupJob
        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        var deleted = await database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM action_log_entries WHERE created_at < {deleteCutoff}", cancellationToken);

        if (deleted > 0)
            logger.LogInformation("Old action log clearing deleted: {Deleted} row(s)", deleted);
    }
}
