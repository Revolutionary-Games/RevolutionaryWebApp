namespace RevolutionaryWebApp.Server.Jobs;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models.Enums;

public class RemoveAdminWithoutTwoFactorJob(ILogger<RemoveAdminWithoutTwoFactorJob> logger,
    ApplicationDbContext database, IBackgroundJobClient jobClient) : IJob
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        var users = await database.Users.Include(user => user.Groups).Include(user => user.TwoFactorAuthentication)
            .Where(user => user.Groups.Any(group => group.Id == GroupType.Admin) &&
                (user.TwoFactorAuthentication == null || !user.TwoFactorAuthentication.Enabled))
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            var adminGroup = user.Groups.First(group => group.Id == GroupType.Admin);
            user.Groups.Remove(adminGroup);

            database.AdminActions.Add(new AdminAction(
                "Automatically removed from Admin group because two-factor authentication is not enabled")
            {
                TargetUserId = user.Id,
            });

            user.OnGroupsChanged(jobClient);
            logger.LogWarning("Removed Admin group from user {Email} because two-factor authentication is not enabled",
                user.Email);
        }

        if (users.Count > 0)
            await database.SaveChangesAsync(cancellationToken);
    }
}
