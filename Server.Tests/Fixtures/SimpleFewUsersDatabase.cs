namespace ThriveDevCenter.Server.Tests.Fixtures;

using System;
using System.Collections.Generic;
using Moq;
using Server.Models;
using Server.Services;
using Shared.Models;
using Shared.Models.Enums;

public class SimpleFewUsersDatabase : BaseSharedDatabaseFixture
{
    internal static readonly Guid StaticSessionId1 = Guid.NewGuid();
    internal static readonly Guid StaticSessionId2 = Guid.NewGuid();
    internal static readonly Guid StaticSessionId3 = Guid.NewGuid();
    internal static readonly Guid StaticSessionId4 = Guid.NewGuid();

    private static readonly object Lock = new();

    private static bool databaseInitialized;

    public SimpleFewUsersDatabase() : base("SimpleFewUsersDatabase")
    {
        lock (Lock)
        {
            if (!databaseInitialized)
            {
                Seed();
                databaseInitialized = true;
            }
        }
    }

    public Guid SessionId1 => StaticSessionId1;
    public Guid SessionId2 => StaticSessionId2;
    public Guid SessionId3 => StaticSessionId3;
    public Guid SessionId4 => StaticSessionId4;

    internal static void SeedUsers(ApplicationDbContext database)
    {
        var user1 = new User
        {
            Id = 1,
            Email = "test@example.com",
            Name = "test",
            Local = true,
        };

        database.Users.Add(user1);

        var user2 = new User
        {
            Id = 2,
            Email = "test2@example.com",
            Name = "test2",
            Local = true,
            Groups = new List<UserGroup>
            {
                database.UserGroups.Find(GroupType.Developer) ?? throw new Exception("Developer group missing"),
            },
        };

        database.Users.Add(user2);

        var user3 = new User
        {
            Id = 3,
            Email = "test3@example.com",
            Name = "test3",
            Local = true,
            Groups = new List<UserGroup>
            {
                database.UserGroups.Find(GroupType.Admin) ?? throw new Exception("Admin group missing"),
            },
        };

        database.Users.Add(user3);

        var user4 = new User
        {
            Id = 4,
            Email = "test4@example.com",
            Name = "test4",
            Local = true,
            Groups = new List<UserGroup>
            {
                database.UserGroups.Find(GroupType.RestrictedUser) ?? throw new Exception("Restricted group missing"),
            },
        };

        database.Users.Add(user4);

        database.SaveChanges();

        if (user4.ProcessGroupDataFromLoadedGroups().ComputePrimaryGroup() != GroupType.RestrictedUser)
            throw new Exception("Unexpected access level for user 4");

        if (user1.ProcessGroupDataFromLoadedGroups().ComputePrimaryGroup() != GroupType.User)
            throw new Exception("Unexpected access level for user 1");

        if (user3.ProcessGroupDataFromLoadedGroups().ComputePrimaryGroup() != GroupType.Admin)
            throw new Exception("Unexpected access level for user 3");

        database.Sessions.Add(new Session
        {
            Id = StaticSessionId1,
            User = user1,
            UserId = user1.Id,
            CachedUserGroups = user1.ProcessGroupDataFromLoadedGroups(),
        });

        database.Sessions.Add(new Session
        {
            Id = StaticSessionId2,
            User = user2,
            UserId = user2.Id,
            CachedUserGroups = user2.ProcessGroupDataFromLoadedGroups(),
        });

        database.Sessions.Add(new Session
        {
            Id = StaticSessionId3,
            User = user3,
            UserId = user3.Id,
            CachedUserGroups = user3.ProcessGroupDataFromLoadedGroups(),
        });

        database.Sessions.Add(new Session
        {
            Id = StaticSessionId4,
            User = user4,
            UserId = user4.Id,
            CachedUserGroups = user4.ProcessGroupDataFromLoadedGroups(),
        });

        database.SaveChanges();
    }

    protected sealed override void Seed()
    {
        AddDefaultGroups();

        SeedUsers(Database);
    }
}

public class SimpleFewUsersDatabaseWithNotifications : BaseSharedDatabaseFixtureWithNotifications
{
    private static readonly object Lock = new();
    private static bool databaseInitialized;

    public SimpleFewUsersDatabaseWithNotifications() : base(
        new Mock<IModelUpdateNotificationSender>().Object, "SimpleFewUsersDatabaseWithNotifications")
    {
        lock (Lock)
        {
            if (!databaseInitialized)
            {
                Seed();
                databaseInitialized = true;
            }
        }
    }

    protected sealed override void Seed()
    {
        AddDefaultGroups();

        SimpleFewUsersDatabase.SeedUsers(Database);
    }
}
