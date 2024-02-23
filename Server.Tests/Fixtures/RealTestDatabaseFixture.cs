namespace RevolutionaryWebApp.Server.Tests.Fixtures;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Shared.Models.Enums;

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

    /// <summary>
    ///   Creates a test storage item (doesn't call save on the db context)
    /// </summary>
    /// <returns>The created item</returns>
    public async Task<StorageItem> CreateTestStorageItem(bool uploaded = true, Random? random = null)
    {
        random ??= new Random();

        var name = $"DummyTest-{random.Next()}-{random.Next()}";

        var versionFile = new StorageFile
        {
            StoragePath = name,
            Size = 123,
            Uploading = !uploaded,
        };

        await Database.StorageFiles.AddAsync(versionFile);

        var itemVersion = new StorageItemVersion
        {
            StorageFile = versionFile,
            Uploading = !uploaded,
        };

        await Database.StorageItemVersions.AddAsync(itemVersion);

        var item = new StorageItem
        {
            Name = name,
            StorageItemVersions = new List<StorageItemVersion> { itemVersion },
        };

        await Database.StorageItems.AddAsync(item);

        return item;
    }

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
        var user1 = new User("test@example.com", "test")
        {
            Id = 1,
            Local = true,
        };

        Database.Users.Add(user1);

        Database.Sessions.Add(new Session
        {
            Id = SessionId1,
            User = user1,
        });

        var user2 = new User("test2@example.com", "test2")
        {
            Id = 2,
            Local = true,
            Groups = new List<UserGroup>
            {
                Database.UserGroups.Find(GroupType.Developer) ?? throw new Exception("Developer group missing"),
            },
        };

        Database.Users.Add(user2);

        Database.Sessions.Add(new Session
        {
            Id = SessionId2,
            User = user2,
        });

        var user3 = new User("test3@example.com", "test3")
        {
            Id = 3,
            Local = true,
            Groups = new List<UserGroup>
            {
                Database.UserGroups.Find(GroupType.Admin) ?? throw new Exception("Admin group missing"),
            },
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
