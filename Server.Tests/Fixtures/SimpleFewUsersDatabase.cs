namespace ThriveDevCenter.Server.Tests.Fixtures;

using System;
using System.Collections.Generic;
using Server.Models;
using Shared.Models;
using Shared.Models.Enums;

public class SimpleFewUsersDatabase : BaseSharedDatabaseFixture
{
    private static readonly object Lock = new();

    private static readonly Guid StaticSessionId1 = Guid.NewGuid();
    private static readonly Guid StaticSessionId2 = Guid.NewGuid();
    private static readonly Guid StaticSessionId3 = Guid.NewGuid();
    private static readonly Guid StaticSessionId4 = Guid.NewGuid();

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

    protected sealed override void Seed()
    {
        var user1 = new User
        {
            Id = 1,
            Email = "test@example.com",
            Name = "test",
            Local = true,
        };

        Database.Users.Add(user1);

        var user2 = new User
        {
            Id = 2,
            Email = "test2@example.com",
            Name = "test2",
            Local = true,
            Groups = new List<UserGroup>
            {
                Database.UserGroups.Find(GroupType.Developer) ?? throw new Exception("Developer group missing"),
            },
        };

        Database.Users.Add(user2);

        var user3 = new User
        {
            Id = 3,
            Email = "test3@example.com",
            Name = "test3",
            Local = true,
            Groups = new List<UserGroup>
            {
                Database.UserGroups.Find(GroupType.Admin) ?? throw new Exception("Admin group missing"),
            },
        };

        Database.Users.Add(user3);

        var user4 = new User
        {
            Id = 4,
            Email = "test4@example.com",
            Name = "test4",
            Local = true,
            Groups = new List<UserGroup>
            {
                Database.UserGroups.Find(GroupType.RestrictedUser) ?? throw new Exception("Restricted group missing"),
            },
        };

        Database.Users.Add(user4);

        Database.SaveChanges();

        if (user4.ComputeUserGroups(Database).Result.ComputePrimaryGroup() != GroupType.RestrictedUser)
            throw new Exception("Unexpected access level for user 4");

        if (user4.ComputeUserGroups(Database).Result.ComputePrimaryGroup() != GroupType.User)
            throw new Exception("Unexpected access level for user 3");

        Database.Sessions.Add(new Session
        {
            Id = SessionId1,
            User = user1,
            CachedUserGroups = user1.ComputeUserGroups(Database).Result,
        });

        Database.Sessions.Add(new Session
        {
            Id = SessionId2,
            User = user2,
            CachedUserGroups = user2.ComputeUserGroups(Database).Result,
        });

        Database.Sessions.Add(new Session
        {
            Id = SessionId3,
            User = user3,
            CachedUserGroups = user3.ComputeUserGroups(Database).Result,
        });

        Database.Sessions.Add(new Session
        {
            Id = SessionId4,
            User = user4,
            CachedUserGroups = user4.ComputeUserGroups(Database).Result,
        });

        Database.SaveChanges();
    }
}
