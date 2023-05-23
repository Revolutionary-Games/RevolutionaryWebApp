namespace ThriveDevCenter.Server.Tests.Utilities;

using Server.Models;
using Shared.Models;
using Shared.Models.Enums;

public static class UserUtilities
{
    /// <summary>
    ///   Creates a dummy user for some use cases
    /// </summary>
    /// <param name="id">The id to set in the user</param>
    /// <returns>The user object to use in tests</returns>
    public static User CreateDeveloperUser(long id)
    {
        var user = new User
        {
            Id = id,
            Email = "test@example.com",
            Name = "Test Developer",
        };

        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.Developer, GroupType.User));

        return user;
    }

    public static User CreateNormalUser(long id)
    {
        var user = new User
        {
            Id = id,
            Email = "test@example.com",
            Name = "Test User",
        };

        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));

        return user;
    }

    public static User CreateAdminUser(long id)
    {
        var user = new User
        {
            Id = id,
            Email = "test@example.com",
            Name = "Example admin",
        };

        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.Developer, GroupType.Admin, GroupType.User));

        return user;
    }
}
