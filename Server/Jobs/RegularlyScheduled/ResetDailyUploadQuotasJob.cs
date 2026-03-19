namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Controllers.Pages;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using SharedBase.Utilities;

/// <summary>
///   Resets the daily upload quota for all users.
///   Writes a log entry if a user is above 50% of their quota.
/// </summary>
[DisableConcurrentExecution(1800)]
public class ResetDailyUploadQuotasJob : IJob
{
    private readonly ILogger<ResetDailyUploadQuotasJob> logger;
    private readonly ApplicationDbContext database;

    public ResetDailyUploadQuotasJob(ILogger<ResetDailyUploadQuotasJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var usersToReset = await database.Users.Where(u => u.UploadQuotaUsed > 0).ToListAsync(cancellationToken);

        bool changes = false;

        foreach (var user in usersToReset)
        {
            var quota = MediaFileController.GetUploadQuotaForUser(user);
            if (quota > 0)
            {
                logger.LogInformation("User {Email} used {Used} out of {Quota} quota today ({Percent:P})",
                    user.Email, user.UploadQuotaUsed, quota, (double)user.UploadQuotaUsed / quota);

                if (user.UploadQuotaUsed > quota / 2)
                {
                    await database.LogEntries.AddAsync(new LogEntry(
                            $"User {user.UserName} used {user.UploadQuotaUsed.BytesToMiB()} out of " +
                            $"{quota.BytesToMiB()} quota today"),
                        cancellationToken);
                }
            }

            user.UploadQuotaUsed = 0;
            changes = true;
        }

        if (changes)
        {
            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Daily upload quotas have been reset for {Count} users", usersToReset.Count);
        }
    }
}
