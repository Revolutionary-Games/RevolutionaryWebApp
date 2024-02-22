namespace RevolutionaryWebApp.Server.Tests.Fixtures;

using System;
using Microsoft.Extensions.Configuration;

public sealed class RealUnitTestDatabaseFixture : RealTestDatabaseFixture
{
    private static readonly object Lock = new();
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
