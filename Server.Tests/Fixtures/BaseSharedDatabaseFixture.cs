namespace RevolutionaryWebApp.Server.Tests.Fixtures;

using System;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Services;
using Shared.Models.Enums;

public abstract class BaseSharedDatabaseFixture : IDisposable
{
    protected BaseSharedDatabaseFixture(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        Database = new ApplicationDbContext(options);
    }

    protected BaseSharedDatabaseFixture(NotificationsEnabledDb notificationsEnabledDb)
    {
        Database = notificationsEnabledDb;
    }

    public ApplicationDbContext Database { get; }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Seed();

    protected void AddDefaultGroups()
    {
        Database.UserGroups.Add(new UserGroup(GroupType.Admin, "Admin"));
        Database.UserGroups.Add(new UserGroup(GroupType.Developer, "Developer"));
        Database.UserGroups.Add(new UserGroup(GroupType.RestrictedUser, "RestrictedUser"));
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Database.Dispose();
        }
    }
}

public abstract class BaseSharedDatabaseFixtureWithNotifications : BaseSharedDatabaseFixture
{
    protected BaseSharedDatabaseFixtureWithNotifications(IModelUpdateNotificationSender notificationSender,
        string dbName) : base(new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(dbName).EnableSensitiveDataLogging()
        .Options, notificationSender))
    {
        NotificationsEnabledDatabase = (NotificationsEnabledDb)Database;
    }

    public NotificationsEnabledDb NotificationsEnabledDatabase { get; }
}
