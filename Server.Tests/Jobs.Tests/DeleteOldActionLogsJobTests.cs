namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class DeleteOldActionLogsJobTests : IClassFixture<RealUnitTestDatabaseFixture>, IDisposable
{
    private readonly XunitLogger<DeleteOldActionLogsJob> logger;
    private readonly RealUnitTestDatabaseFixture fixture;

    public DeleteOldActionLogsJobTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<DeleteOldActionLogsJob>(output);
    }

    [Fact]
    public async Task DeleteOldActionLogsJob_DeletesRightLogEntries()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        var log1 = new ActionLogEntry("Log message 1")
        {
            CreatedAt = DateTime.UtcNow - TimeSpan.FromSeconds(30),
        };
        await database.ActionLogEntries.AddAsync(log1);

        var log2 = new ActionLogEntry("Log message 2")
        {
            CreatedAt = DateTime.UtcNow - TimeSpan.FromDays(10),
        };
        await database.ActionLogEntries.AddAsync(log2);

        var log3 = new ActionLogEntry("Log message 3")
        {
            CreatedAt = DateTime.UtcNow - AppInfo.DeleteActionLogsAfter - TimeSpan.FromSeconds(30),
        };
        await database.ActionLogEntries.AddAsync(log3);

        await database.SaveChangesAsync();

        var countBefore = await database.ActionLogEntries.CountAsync();

        var job = new DeleteOldActionLogsJob(logger, database);
        await job.Execute(CancellationToken.None);

        Assert.NotNull(await ReadWithRawSql(log1.Id));
        Assert.NotNull(await ReadWithRawSql(log2.Id));
        Assert.Null(await ReadWithRawSql(log3.Id));
        Assert.Equal(countBefore - 1, await database.ActionLogEntries.CountAsync());
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private Task<ActionLogEntry?> ReadWithRawSql(long id)
    {
        // See the comments in SessionCleanupJobTests
        return fixture.Database.ActionLogEntries
            .FromSqlInterpolated($"SELECT * FROM action_log_entries WHERE id = {id}").FirstOrDefaultAsync();
    }
}
