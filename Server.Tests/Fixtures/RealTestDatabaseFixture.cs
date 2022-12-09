namespace ThriveDevCenter.Server.Tests.Fixtures;

using System;
using Microsoft.EntityFrameworkCore;
using Server.Models;

/// <summary>
///   A real postgresql database used in tests.
///   You should use the integration test or unit test variant instead of this directly.
/// </summary>
public abstract class RealTestDatabaseFixture : IDisposable
{
    protected RealTestDatabaseFixture(string connectionString)
    {
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("connection string is empty, make sure to setup secrets",
                nameof(connectionString));
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        Database = new ApplicationDbContext(options);
    }

    public static Guid SessionId1 { get; } = Guid.NewGuid();
    public static Guid SessionId2 { get; } = Guid.NewGuid();
    public static Guid SessionId3 { get; } = Guid.NewGuid();

    public ApplicationDbContext Database { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected void RecreateDb()
    {
        Database.Database.EnsureDeleted();
        Database.Database.EnsureCreated();
    }

    protected void InsertBasicUsers()
    {
        var user1 = new User
        {
            Id = 1,
            Email = "test@example.com",
            Name = "test",
            Local = true,
        };

        Database.Users.Add(user1);

        Database.Sessions.Add(new Session
        {
            Id = SessionId1,
            User = user1,
        });

        var user2 = new User
        {
            Id = 2,
            Email = "test2@example.com",
            Name = "test2",
            Local = true,
            Developer = true,
        };

        Database.Users.Add(user2);

        Database.Sessions.Add(new Session
        {
            Id = SessionId2,
            User = user2,
        });

        var user3 = new User
        {
            Id = 3,
            Email = "test3@example.com",
            Name = "test3",
            Local = true,
            Admin = true,
        };

        Database.Users.Add(user3);

        Database.Sessions.Add(new Session
        {
            Id = SessionId3,
            User = user3,
        });
    }

    protected abstract void Seed();

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Database.Dispose();
        }
    }
}
