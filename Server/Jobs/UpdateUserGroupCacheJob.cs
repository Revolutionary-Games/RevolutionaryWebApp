namespace RevolutionaryWebApp.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Updates the cached user groups for a user, MUST BE EXECUTED when user's groups change (done by calling
///   <see cref="User.OnGroupsChanged"/>)
/// </summary>
public class UpdateUserGroupCacheJob
{
    private readonly ILogger<UpdateUserGroupCacheJob> logger;
    private readonly ApplicationDbContext database;

    public UpdateUserGroupCacheJob(ILogger<UpdateUserGroupCacheJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(long userId, CancellationToken cancellationToken)
    {
        var targetUser = await database.Users.FindAsync(userId);

        if (targetUser == null)
        {
            logger.LogError("Cannot update cached groups for user that can't be found: {UserId}", userId);
            return;
        }

        var dummySession = new Session
        {
            UserId = userId,
        };

        cancellationToken.ThrowIfCancellationRequested();

        dummySession.CachedUserGroups = await targetUser.ComputeUserGroups(database);

        var cachedGroups = dummySession.CachedUserGroupsRaw;

        var updated = await database.Database.ExecuteSqlAsync(
            $"UPDATE sessions SET cached_user_groups_raw = {cachedGroups} WHERE user_id = {userId};",
            cancellationToken);

        logger.LogInformation("Updated {Count} cached groups in sessions for user {UserId}", updated, userId);

        // Clear launcher link cached groups, these are cleared as these are recomputed on demand
        updated = await database.Database.ExecuteSqlAsync(
            $"UPDATE launcher_links SET cached_user_groups_raw = NULL WHERE user_id = {userId};",
            cancellationToken);

        if (updated > 0)
        {
            logger.LogInformation("Updated {Count} cached groups in launcher links for user {UserId}",
                updated, userId);
        }
    }
}
