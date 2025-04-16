namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Utilities;

public class ApplyUserAutomaticGroupsJob(ILogger<ApplyUserAutomaticGroupsJob> logger, ApplicationDbContext database,
    IBackgroundJobClient jobClient)
{
    public async Task Execute(string email, CancellationToken cancellationToken)
    {
        var user = await database.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user == null)
        {
            logger.LogWarning("Can't apply automatic groups to user that doesn't exist: {Email}", email);
            return;
        }

        user.ProcessGroupDataFromLoadedGroups();

        var patreonSettings =
            new Lazy<Task<PatreonSettings>>(async () => await database.PatreonSettings.OrderBy(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? throw new Exception("Patreon settings not found"));

        var changes = await UserGroupApplyHandler.ApplyUserGroupsIfNeeded(user, database, logger, patreonSettings,
            jobClient, cancellationToken);

        if (changes)
        {
            logger.LogInformation("Applied automatic groups to user {Email}, saving changes", email);
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
