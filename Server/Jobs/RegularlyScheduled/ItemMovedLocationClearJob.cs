namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

/// <summary>
///   Clears the moved from locations of items that haven't been changed in a while
/// </summary>
[DisableConcurrentExecution(1800)]
public class ItemMovedLocationClearJob : IJob
{
    private readonly ILogger<ItemMovedLocationClearJob> logger;
    private readonly ApplicationDbContext database;

    public ItemMovedLocationClearJob(ILogger<ItemMovedLocationClearJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var clearCutoff = DateTime.UtcNow - AppInfo.RemoveMovedFromInfoAfter;

        // Increase timeout to make sure this doesn't fail, see the note in SessionCleanupJob about this
        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        var modified = await database.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE storage_items SET moved_from_location = NULL WHERE updated_at < {clearCutoff} AND 
               moved_from_location IS NOT NULL", cancellationToken);

        if (modified > 0)
            logger.LogInformation("Old move locations clean finished and removed: {Modified} location(s)", modified);
    }
}
