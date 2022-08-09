namespace AutomatedUITests.Fixtures;

using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ThriveDevCenter.Server.Models;

public sealed class RealIntegrationTestDatabaseFixture
{
    private static readonly object Lock = new object();
    private static bool databaseInitialized;

    public RealIntegrationTestDatabaseFixture()
    {
        var connectionString = GetConnectionString();

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("connection string is empty, make sure to setup secrets",
                nameof(connectionString)
            );
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        Database = new ApplicationDbContext(options);

        lock (Lock)
        {
            if (!databaseInitialized)
            {
                Database.Database.EnsureDeleted();
                Database.Database.EnsureCreated();
                Seed();
                databaseInitialized = true;
            }
        }
    }

    public ApplicationDbContext Database { get; }

    public static string GetConnectionString()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<RealIntegrationTestDatabaseFixture>().Build();

        return configuration["IntegrationTestConnection"];
    }

    public void Dispose()
    {
        Database.Dispose();
    }

    private void Seed()
    {
    }
}