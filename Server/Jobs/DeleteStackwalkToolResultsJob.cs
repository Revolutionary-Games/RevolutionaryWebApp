namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

public class DeleteStackwalkToolResultsJob : IJob
{
    private readonly ILogger<DeleteStackwalkToolResultsJob> logger;
    private readonly NotificationsEnabledDb database;

    public DeleteStackwalkToolResultsJob(ILogger<DeleteStackwalkToolResultsJob> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.KeepStackwalkResultsFor;

        var finishedTasks = await database.StackwalkTasks.Where(s => s.FinishedAt != null && s.FinishedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (finishedTasks.Count > 0)
        {
            logger.LogInformation("Cleaning finished stackwalk tasks, count: {Count}", finishedTasks.Count);

            database.StackwalkTasks.RemoveRange(finishedTasks);
            await database.SaveChangesAsync(cancellationToken);
        }

        var cutoff2 = DateTime.UtcNow - AppInfo.DeleteFailedStackwalkAttemptsAfter;

        var failedTasks = await database.StackwalkTasks.Where(s => s.CreatedAt < cutoff2)
            .ToListAsync(cancellationToken);

        if (failedTasks.Count > 0)
        {
            logger.LogWarning("Cleaning entirely failed stackwalk tasks, count: {Count}", failedTasks.Count);

            database.StackwalkTasks.RemoveRange(failedTasks);

            await database.LogEntries.AddAsync(new LogEntry
            {
                Message = $"Cleared {failedTasks.Count} stackwalk tasks that failed to run entirely",
            }, cancellationToken);

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}