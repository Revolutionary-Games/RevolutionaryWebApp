namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models;

/// <summary>
///   Detects if HandleControlledServersJobsJob hasn't run correctly and stopped server instances
/// </summary>
public class DetectLeftOnServersJob : IJob
{
    private readonly ILogger<DetectLeftOnServersJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;
    private readonly TimeSpan detectionTime;

    public DetectLeftOnServersJob(ILogger<DetectLeftOnServersJob> logger, IConfiguration configuration,
        NotificationsEnabledDb database, IBackgroundJobClient jobClient)
    {
        var idleDelay = TimeSpan.FromSeconds(Convert.ToInt32(configuration["CI:ServerIdleTimeBeforeStop"]));

        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;

        var defaultTime = TimeSpan.FromMinutes(15);
        var idleTimesFour = idleDelay * 4;
        detectionTime = idleTimesFour > defaultTime ? idleTimesFour : defaultTime;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - detectionTime;

        bool jobNeeded = false;

        foreach (var server in await database.ControlledServers.Where(s =>
                     s.UpdatedAt < cutoff && s.Status == ServerStatus.Running).ToListAsync(cancellationToken))
        {
            logger.LogWarning("Server {Id} has been left running, last updated: {UpdatedAt}",
                server.Id, server.UpdatedAt);

            jobNeeded = true;
        }

        if (jobNeeded)
        {
            await database.LogEntries.AddAsync(
                new LogEntry("Detected servers that are left in running state, trying to fix by re-running handle job"),
                cancellationToken);

            await database.SaveChangesAsync(cancellationToken);

            jobClient.Enqueue<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None));
        }
    }
}
