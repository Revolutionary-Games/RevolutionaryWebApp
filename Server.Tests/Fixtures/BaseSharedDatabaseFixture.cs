namespace ThriveDevCenter.Server.Tests.Fixtures
{
    using System;
    using Microsoft.EntityFrameworkCore;
    using Server.Models;

    public abstract class BaseSharedDatabaseFixture : IDisposable
    {
        protected BaseSharedDatabaseFixture(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

            Database = new ApplicationDbContext(options);
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
}
