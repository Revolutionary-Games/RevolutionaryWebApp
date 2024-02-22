namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Server.Jobs;
using Server.Models;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class SessionCleanupJobTests : IClassFixture<RealUnitTestDatabaseFixture>, IDisposable
{
    private readonly XunitLogger<SessionCleanupJob> logger;
    private readonly RealUnitTestDatabaseFixture fixture;

    public SessionCleanupJobTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<SessionCleanupJob>(output);
    }

    [Fact]
    public async Task SessionCleanup_DeletesOnlyOldSessions()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        var created1 = new Session
        {
            LastUsed = DateTime.UtcNow - TimeSpan.FromDays(10),
        };

        await database.Sessions.AddAsync(created1);
        await database.SaveChangesAsync();

        // Store count before we add the to be deleted items
        // This doesn't need raw SQL as the count async seems to always go to the DB anyway
        var countBefore = await database.Sessions.CountAsync();

        var created2 = new Session
        {
            LastUsed = DateTime.UtcNow - TimeSpan.FromDays(35),
        };

        await database.Sessions.AddAsync(created2);
        await database.SaveChangesAsync();

        Assert.NotNull(await database.Sessions.FindAsync(created2.Id));
        Assert.NotNull(await database.Sessions.FindAsync(created1.Id));

        // Run the job
        var job = new SessionCleanupJob(logger, database);
        await job.Execute(default);

        // To detect the removal, we need actually a different db context, but due to the transaction, it wouldn't
        // see the changes, so we fallback on raw SQL
        var retrieved1 = await database.Sessions
            .FromSqlInterpolated($"SELECT * FROM sessions WHERE id = {created1.Id}").FirstOrDefaultAsync();
        Assert.NotNull(retrieved1);

        var retrieved2 = await database.Sessions
            .FromSqlInterpolated($"SELECT * FROM sessions WHERE id = {created2.Id}").FirstOrDefaultAsync();

        Assert.Null(retrieved2);
        Assert.Equal(countBefore, await database.Sessions.CountAsync());
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
