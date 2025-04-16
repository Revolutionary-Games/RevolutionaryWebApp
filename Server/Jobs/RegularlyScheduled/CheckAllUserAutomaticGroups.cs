namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models.Enums;
using Utilities;

/// <summary>
///   Verifies that all users have their correct groups applied like patron status
/// </summary>
public class CheckAllUserAutomaticGroups(ILogger<CheckAllUserAutomaticGroups> logger, ApplicationDbContext database,
    IBackgroundJobClient jobClient)
    : IJob
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        var patreonSettings = await database.PatreonSettings.AsNoTracking().OrderBy(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (patreonSettings == null)
        {
            logger.LogWarning("Patreon settings not found, skipping automatic group application");
            return;
        }

        // TODO: patron email aliases
        var patronEmails = await database.Patrons.Select(p => p.Email).ToHashSetAsync(cancellationToken);

        var patreonGroup =
            await database.UserGroups.FirstOrDefaultAsync(g => g.Id == GroupType.PatreonSupporter,
                cancellationToken: cancellationToken) ?? throw new Exception("Patreon group not found");

        var patreonSettingsWrapper = new Lazy<Task<PatreonSettings>>(() => Task.FromResult(patreonSettings));

        // Load all users that may need Patreon applying or removing
        var users = await database.Users.Where(u => patronEmails.Contains(u.Email) || u.Groups.Contains(patreonGroup))
            .ToListAsync(cancellationToken);

        bool changes = false;

        foreach (var user in users)
        {
            if (await UserGroupApplyHandler.ApplyUserGroupsIfNeeded(user, database, logger, patreonSettingsWrapper,
                    jobClient, cancellationToken))
            {
                logger.LogInformation("Applied automatic group changes to {Email}", user.Email);
                changes = true;
            }
        }

        if (changes)
        {
            logger.LogInformation("Applied automatic groups to users, saving changes");
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
