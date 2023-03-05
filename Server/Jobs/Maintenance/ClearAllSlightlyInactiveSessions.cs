namespace ThriveDevCenter.Server.Jobs.Maintenance;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

public class ClearAllSlightlyInactiveSessions : MaintenanceJobBase
{
    public ClearAllSlightlyInactiveSessions(ILogger<ClearAllSlightlyInactiveSessions> logger,
        ApplicationDbContext operationDb, NotificationsEnabledDb operationStatusDb) : base(logger, operationDb,
        operationStatusDb)
    {
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        var deleteCutoff = DateTime.UtcNow - TimeSpan.FromHours(1);

        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        var deleted = await database.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM sessions WHERE last_used < {deleteCutoff}", cancellationToken);

        logger.LogInformation("Session maintenance for all slightly inactive sessions deleted: {Deleted} row(s)",
            deleted);
        operationData.ExtendedDescription = $"Deleted {deleted} row(s)";
    }
}
