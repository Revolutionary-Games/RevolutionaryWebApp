namespace RevolutionaryWebApp.Shared.Models;

using Enums;

public static class APIKeyPolicy
{
    public static bool IsReadOnlyUser(IUserGroupData groups)
    {
        return !groups.HasAccessLevel(GroupType.Developer) && !groups.HasGroup(GroupType.PatreonSupporter);
    }

    public static int Limit(IUserGroupData groups)
    {
        if (groups.HasGroup(GroupType.RestrictedUser))
            return 0;
        if (groups.HasAccessLevel(GroupType.Admin))
            return 20;
        if (groups.HasAccessLevel(GroupType.Developer))
            return 5;

        return groups.HasGroup(GroupType.PatreonSupporter) ? 3 : 1;
    }
}
