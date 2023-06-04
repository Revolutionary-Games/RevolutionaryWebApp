namespace ThriveDevCenter.Server.Jobs.RegularlyScheduled;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

public class DeleteOldServerLogsJob : IJob
{
    private readonly ILogger<DeleteOldServerLogsJob> logger;
    private readonly ApplicationDbContext database;

    public DeleteOldServerLogsJob(ILogger<DeleteOldServerLogsJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var deleteCutoff = DateTime.UtcNow - AppInfo.DeleteServerLogsAfter;

        // See the comment in SessionCleanupJob
        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        var deleted = await database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM log_entries WHERE created_at < {deleteCutoff}", cancellationToken);

        if (deleted > 0)
            logger.LogInformation("Old server log clearing deleted: {Deleted} row(s)", deleted);
    }
}
