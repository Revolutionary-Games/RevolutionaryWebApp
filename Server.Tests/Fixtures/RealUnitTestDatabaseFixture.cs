namespace RevolutionaryWebApp.Server.Tests.Fixtures;

using System;
using System.Threading;
using Microsoft.Extensions.Configuration;

public sealed class RealUnitTestDatabaseFixture : RealTestDatabaseFixture
{
    private static readonly Lock Lock = new();
    private static bool databaseInitialized;

    public RealUnitTestDatabaseFixture() : base(GetConnectionString())
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

    /// <summary>
    ///   Allows getting database connection string for additional connections, which are needed by some tests.
    /// </summary>
    /// <returns>Connection string to connect to the same database as this</returns>
    public string GetConnectionStringForAdditionalDatabase()
    {
        return GetConnectionString();
    }

    protected override void Seed()
    {
        RecreateDb();

        InsertBasicUsers();

        Database.SaveChanges();
    }

    private static string GetConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<RealUnitTestDatabaseFixture>().Build();

        return configuration["UnitTestConnection"] ??
            throw new Exception("Failed to get unit test DB connection string");
    }
}
