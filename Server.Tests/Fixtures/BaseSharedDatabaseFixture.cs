namespace ThriveDevCenter.Server.Tests.Fixtures;

using System;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Services;

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

    protected abstract void Seed();

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            Database.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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