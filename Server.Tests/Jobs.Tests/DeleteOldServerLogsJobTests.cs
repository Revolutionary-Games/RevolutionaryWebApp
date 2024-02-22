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

public sealed class DeleteOldServerLogsJobTests : IClassFixture<RealUnitTestDatabaseFixture>, IDisposable
{
    private readonly XunitLogger<DeleteOldServerLogsJob> logger;
    private readonly RealUnitTestDatabaseFixture fixture;

    public DeleteOldServerLogsJobTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<DeleteOldServerLogsJob>(output);
    }

    [Fact]
    public async Task DeleteOldServerLogsJob_DeletesRightLogEntries()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        var log1 = new LogEntry
        {
            Message = "Log message 1",
            CreatedAt = DateTime.UtcNow - TimeSpan.FromSeconds(30),
        };
        await database.LogEntries.AddAsync(log1);

        var log2 = new LogEntry
        {
            Message = "Log message 2",
            CreatedAt = DateTime.UtcNow - TimeSpan.FromDays(10),
        };
        await database.LogEntries.AddAsync(log2);

        var log3 = new LogEntry
        {
            Message = "Log message 3",
            CreatedAt = DateTime.UtcNow - AppInfo.DeleteServerLogsAfter - TimeSpan.FromSeconds(30),
        };
        await database.LogEntries.AddAsync(log3);

        await database.SaveChangesAsync();

        var countBefore = await database.LogEntries.CountAsync();

        var job = new DeleteOldServerLogsJob(logger, database);
        await job.Execute(CancellationToken.None);

        Assert.NotNull(await ReadWithRawSql(log1.Id));
        Assert.NotNull(await ReadWithRawSql(log2.Id));
        Assert.Null(await ReadWithRawSql(log3.Id));
        Assert.Equal(countBefore - 1, await database.LogEntries.CountAsync());
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private Task<LogEntry?> ReadWithRawSql(long id)
    {
        // See the comments in SessionCleanupJobTests
        return fixture.Database.LogEntries
            .FromSqlInterpolated($"SELECT * FROM log_entries WHERE id = {id}").FirstOrDefaultAsync();
    }
}
