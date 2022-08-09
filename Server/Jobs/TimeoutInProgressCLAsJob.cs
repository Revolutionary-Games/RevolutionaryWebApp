namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

public class TimeoutInProgressClAsJob : IJob
{
    private readonly ILogger<TimeoutInProgressClAsJob> logger;
    private readonly ApplicationDbContext database;

    public TimeoutInProgressClAsJob(ILogger<TimeoutInProgressClAsJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.StartedSigningTimeout;

        var toTimeOut = await database.InProgressClaSignatures.Where(s => s.CreatedAt < cutoff)
            .ToListAsync(cancellationToken);

        if (toTimeOut.Count < 1)
            return;

        database.InProgressClaSignatures.RemoveRange(toTimeOut);

        await database.SaveChangesAsync(cancellationToken);

        foreach (var item in toTimeOut)
            logger.LogInformation("Timed out in-progress signature in session {SessionId}", item.SessionId);
    }
}