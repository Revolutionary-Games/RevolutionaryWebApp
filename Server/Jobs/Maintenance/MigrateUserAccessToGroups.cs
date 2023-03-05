namespace ThriveDevCenter.Server.Jobs.Maintenance;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Enums;

/// <summary>
///   Migrates the bool permission indicators in user to groups
/// </summary>
public class MigrateUserAccessToGroups : MaintenanceJobBase
{
    public MigrateUserAccessToGroups(ILogger<MigrateUserAccessToGroups> logger, ApplicationDbContext operationDb,
        NotificationsEnabledDb operationStatusDb) : base(logger, operationDb, operationStatusDb)
    {
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        var adminGroup = await database.UserGroups.FindAsync(GroupType.Admin);

        if (adminGroup == null)
            throw new Exception("Inbuilt group not found");

        var developerGroup = await database.UserGroups.FindAsync(GroupType.Developer);

        if (developerGroup == null)
            throw new Exception("Inbuilt group not found");

        var restrictedGroup = await database.UserGroups.FindAsync(GroupType.RestrictedUser);

        if (restrictedGroup == null)
            throw new Exception("Inbuilt group not found");

        // All our sites are small enough for memory to be enough here
        var allUsers = await database.Users.Include(u => u.Groups).ToListAsync(cancellationToken);

        int devs = 0;
        int admins = 0;
        int restricted = 0;

        foreach (var user in allUsers)
        {
            if (user.Developer == true)
            {
                logger.LogInformation("Adding user {Name} to developer group", user.Name);
                user.Groups.Add(developerGroup);
                ++devs;
            }

            if (user.Admin == true)
            {
                logger.LogInformation("Adding user {Name} to admin group", user.Name);
                user.Groups.Add(adminGroup);
                ++admins;
            }

            if (user.Restricted)
            {
                logger.LogInformation("Adding user {Name} to restricted group", user.Name);
                user.Groups.Add(restrictedGroup);
                ++restricted;
            }
            else
            {
                if (user.Groups.Remove(restrictedGroup))
                    logger.LogInformation("Removing user {Name} from restricted group", user.Name);
            }
        }

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Saved changes for group migration, devs: {Devs}, admins: {Admins}, restricted: {Restricted}", devs, admins,
            restricted);

        operationData.ExtendedDescription =
            $"Migrated users. Total devs: {devs}, admins: {admins}, restricted: {restricted}";
    }
}
