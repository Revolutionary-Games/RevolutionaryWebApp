namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Clears the suspended until times from users when passed that time
/// </summary>
[DisableConcurrentExecution(1800)]
public class ClearPassedSuspensionTimes : IJob
{
    private readonly ILogger<ClearPassedSuspensionTimes> logger;
    private readonly ApplicationDbContext database;

    public ClearPassedSuspensionTimes(ILogger<ClearPassedSuspensionTimes> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow;

        var usersToClear = await database.Users.Where(u => u.SuspendedUntil != null && u.SuspendedUntil < cutoff)
            .ToListAsync(cancellationToken);

        bool changes = false;

        foreach (var user in usersToClear)
        {
            if (user.SuspendedUntil <= cutoff)
            {
                user.SuspendedUntil = null;
                user.BumpUpdatedAt();
                changes = true;

                logger.LogInformation("User {Email} is no longer suspended, clearing suspended flag", user.Email);
                await database.LogEntries.AddAsync(
                    new LogEntry($"User {user.UserName} is no longer suspended as of today"), cancellationToken);
            }
            else
            {
                logger.LogWarning("Suspension time returned from database was not actually suitable for clearing");
            }
        }

        if (changes)
            await database.SaveChangesAsync(cancellationToken);
    }
}
