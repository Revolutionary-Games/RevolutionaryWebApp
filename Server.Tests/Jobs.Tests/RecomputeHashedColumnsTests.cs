namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

using System;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Server.Jobs;
using Server.Models;
using Server.Utilities;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class RecomputeHashedColumnsTests : IClassFixture<RealUnitTestDatabaseFixture>, IDisposable
{
    private readonly XunitLogger<RecomputeHashedColumns> logger;
    private readonly RealUnitTestDatabaseFixture fixture;

    public RecomputeHashedColumnsTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<RecomputeHashedColumns>(output);
    }

    [Fact]
    public async Task RecomputeHashedColumns_ComputesSessionIdHash()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        var created = new Session
        {
            SsoNonce = "5123",
        };

        // It should be impossible to create a session with no hash, so we use raw SQL to insert it
        // It seems impossible to split the interpolated string here into multiple lines...
        // LineLengthCheckDisable
        var changes =
            await database.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO sessions (id, session_version, last_used, sso_nonce) VALUES ({created.Id}, 1, {created.LastUsed}, {created.SsoNonce});");

        // LineLengthCheckEnable

        Assert.Equal(1, changes);

        created.ComputeHashedLookUpValues();
        Assert.Equal(SelectByHashedProperty.HashForDatabaseValue(created.Id.ToString()), created.HashedId);

        // Run the job
        var job = new RecomputeHashedColumns(logger, database);
        await job.Execute(default);

        var retrieved = await database.Sessions
            .FromSqlInterpolated($"SELECT * FROM sessions WHERE id = {created.Id}").FirstOrDefaultAsync();
        Assert.NotNull(retrieved);

        Assert.Equal(created.HashedId, retrieved.HashedId);
        Assert.Equal(created.SsoNonce, retrieved.SsoNonce);

        Assert.Null(await database.Sessions
            .FromSqlInterpolated($"SELECT * FROM sessions WHERE id = {created.Id} AND hashed_id IS NULL")
            .FirstOrDefaultAsync());
    }

    public void Dispose()
    {
        logger.Dispose();
        fixture.Dispose();
    }
}
