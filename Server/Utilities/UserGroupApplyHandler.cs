namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models;
using Shared.Models.Enums;

public static class UserGroupApplyHandler
{
    /// <summary>
    ///   Checks (and applies) user groups (Patron status)
    /// </summary>
    /// <returns>Returns true when changes have been made and database changes need saving</returns>
    public static async Task<bool> ApplyUserGroupsIfNeeded(User user, ApplicationDbContext database, ILogger logger,
        Lazy<Task<PatreonSettings>> patreonSettings, IBackgroundJobClient jobClient,
        CancellationToken cancellationToken)
    {
        var groups = user.AccessCachedGroupsOrThrow();

        // TODO: email alias handling
        var patron =
            await database.Patrons.FirstOrDefaultAsync(p => p.Email == user.Email, cancellationToken);

        bool isPatron = false;

        if (patron != null && patron.Suspended != true)
        {
            var settings = await patreonSettings.Value;

            if (settings.IsEntitledToDevBuilds(patron))
            {
                isPatron = true;
            }
        }

        var patreonGroup = new Lazy<Task<UserGroup>>(async () =>
            await database.UserGroups.FindAsync([GroupType.PatreonSupporter], cancellationToken) ??
            throw new Exception("Patreon group not found"));

        if (isPatron)
        {
            if (!groups.HasGroup(GroupType.PatreonSupporter))
            {
                await database.LogEntries.AddAsync(new LogEntry("Applied user Patreon group")
                {
                    TargetUserId = user.Id,
                }, cancellationToken);

                logger.LogInformation("User {Email} is a patron, applying group", user.Email);

                user.Groups.Add(await patreonGroup.Value);
                user.OnGroupsChanged(jobClient, false);
                user.BumpUpdatedAt();

                return true;
            }
        }
        else
        {
            if (groups.HasGroup(GroupType.PatreonSupporter))
            {
                await database.LogEntries.AddAsync(new LogEntry("Removed user Patreon group")
                {
                    TargetUserId = user.Id,
                }, cancellationToken);

                logger.LogInformation("User {Email} is not a patron, removing group", user.Email);

                user.Groups.Remove(await patreonGroup.Value);
                user.OnGroupsChanged(jobClient, false);
                user.BumpUpdatedAt();

                return true;
            }
        }

        return false;
    }
}
