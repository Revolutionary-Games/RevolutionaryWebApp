namespace ThriveDevCenter.Server.Jobs;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

public class CheckAssociationStatusForUserJob
{
    private readonly ILogger<CheckAssociationStatusForUserJob> logger;
    private readonly NotificationsEnabledDb database;

    public CheckAssociationStatusForUserJob(ILogger<CheckAssociationStatusForUserJob> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(string email, CancellationToken cancellationToken)
    {
        var user = await database.Users.Where(u => u.Email == email).Include(u => u.AssociationMember)
            .FirstOrDefaultAsync(cancellationToken);

        AssociationMember? associationMember;
        if (user == null)
        {
            logger.LogInformation("No user with email to check association status for");

            // Unset if there is some member pointing to the email
            associationMember = await database.AssociationMembers.Where(a => a.Email == email).Include(a => a.User)
                .FirstOrDefaultAsync(cancellationToken);

            if (associationMember is { UserId: not null })
            {
                logger.LogInformation("Removing link to now missing email from association member {Id}",
                    associationMember.Id);
                associationMember.UserId = null;
                await database.SaveChangesAsync(cancellationToken);
            }

            return;
        }

        associationMember = await database.AssociationMembers.Where(a => a.Email == user.Email)
            .Include(a => a.User).FirstOrDefaultAsync(cancellationToken);

        if (associationMember != null)
        {
            if (associationMember.UserId != user.Id)
            {
                associationMember.UserId = user.Id;

                if (user.AssociationMember != null)
                {
                    logger.LogInformation("Association member {Id} is no longer associated with user {Id2}",
                        user.AssociationMember.Id, user.Id);
                    user.AssociationMember.UserId = null;
                }

                logger.LogInformation("Association member {Id} is now linked with user {Id2}", associationMember.Id,
                    user.Id);
                await database.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            if (user.AssociationMember != null)
            {
                logger.LogInformation("Association member {Id} is no longer associated with user {Id2}",
                    user.AssociationMember.Id, user.Id);
                user.AssociationMember.UserId = null;
                await database.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
