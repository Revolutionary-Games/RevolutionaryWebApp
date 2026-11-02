namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;

/// <summary>
///   Resets active users' daily budgets and grows their limits (up to a ceiling).
/// </summary>
[DisableConcurrentExecution(1800)]
public class ResetDailyContributionsJob : IJob
{
    private readonly ApplicationDbContext database;

    public ResetDailyContributionsJob(ApplicationDbContext database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var userIds = await database.UserContributionCounts.Where(count => count.CurrentContribution > 0)
            .Select(count => count.UserId).ToListAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            var counts = database.UserContributionCounts.Where(count => count.UserId == userId);
            var user = await database.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
            if (user == null)
                continue;

            await user.ComputeUserGroups(database);
            const int maxAttempts = 5;
            for (int attempt = 0; attempt < maxAttempts; ++attempt)
            {
                var count = await counts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
                if (count == null || count.CurrentContribution <= 0)
                    break;

                var previousContribution = count.CurrentContribution;
                var previousLimit = count.ContributionLimit;
                var previouslyExceeded = count.ContributionExceeded;
                count.ResetDaily(user.AccessCachedGroupsOrThrow());

                // This model has no concurrency token. Explicitly compare the values used to calculate the reset
                // so a successful contribution between the read and update causes a retry instead of being lost.
                var updated = await counts.Where(current => current.CurrentContribution == previousContribution &&
                        current.ContributionLimit == previousLimit &&
                        current.ContributionExceeded == previouslyExceeded)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(current => current.CurrentContribution, 0)
                        .SetProperty(current => current.ContributionExceeded, false)
                        .SetProperty(current => current.ContributionLimit, count.ContributionLimit), cancellationToken);
                if (updated > 0)
                    break;

                if (attempt == maxAttempts - 1)
                    throw new DbUpdateConcurrencyException($"Could not reset contribution count for user {userId}");
            }
        }
    }
}
