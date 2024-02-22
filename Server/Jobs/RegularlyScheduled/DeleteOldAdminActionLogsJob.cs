namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

[DisableConcurrentExecution(900)]
public class DeleteOldAdminActionLogsJob : IJob
{
    private readonly ILogger<DeleteOldAdminActionLogsJob> logger;
    private readonly ApplicationDbContext database;

    public DeleteOldAdminActionLogsJob(ILogger<DeleteOldAdminActionLogsJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var deleteCutoff = DateTime.UtcNow - AppInfo.DeleteAdminActionLogsAfter;

        // See the comment in SessionCleanupJob
        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        var deleted = await database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM admin_actions WHERE created_at < {deleteCutoff}", cancellationToken);

        if (deleted > 0)
            logger.LogInformation("Old admin action log clearing deleted: {Deleted} row(s)", deleted);
    }
}
