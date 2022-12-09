namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

public class SessionCleanupJob : IJob
{
    private readonly ILogger<SessionCleanupJob> logger;
    private readonly ApplicationDbContext database;

    public SessionCleanupJob(ILogger<SessionCleanupJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting database sessions cleanup");

        var deleteCutoff = DateTime.UtcNow - TimeSpan.FromSeconds(AppInfo.SessionExpirySeconds);

        // Increase timeout, as it might take a while to cleanup the sessions,
        // and being a cancellable job background job this won't cause problems
        // This doesn't need to be reset as the dependency injected instance is exclusive to us
        database.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));

        var deleted =
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM sessions WHERE last_used < {deleteCutoff}", cancellationToken);

        // TODO: add absolute (based on creation time) of session cutoff for ClientCookieExpirySeconds

        logger.LogInformation("Session cleanup finished, and deleted: {Deleted} row(s)", deleted);
    }
}
