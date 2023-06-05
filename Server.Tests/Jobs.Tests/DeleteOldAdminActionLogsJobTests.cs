namespace ThriveDevCenter.Server.Tests.Jobs.Tests;

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

public sealed class DeleteOldAdminActionLogsJobTests : IClassFixture<RealUnitTestDatabaseFixture>, IDisposable
{
    private readonly XunitLogger<DeleteOldAdminActionLogsJob> logger;
    private readonly RealUnitTestDatabaseFixture fixture;

    public DeleteOldAdminActionLogsJobTests(RealUnitTestDatabaseFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        logger = new XunitLogger<DeleteOldAdminActionLogsJob>(output);
    }

    [Fact]
    public async Task DeleteOldAdminActionLogsJob_DeletesRightLogEntries()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        var log1 = new AdminAction
        {
            Message = "Log message 1",
            CreatedAt = DateTime.UtcNow - TimeSpan.FromSeconds(30),
        };
        await database.AdminActions.AddAsync(log1);

        var log2 = new AdminAction
        {
            Message = "Log message 2",
            CreatedAt = DateTime.UtcNow - TimeSpan.FromDays(10),
        };
        await database.AdminActions.AddAsync(log2);

        var log3 = new AdminAction
        {
            Message = "Log message 3",
            CreatedAt = DateTime.UtcNow - AppInfo.DeleteAdminActionLogsAfter - TimeSpan.FromSeconds(30),
        };
        await database.AdminActions.AddAsync(log3);

        var log4 = new AdminAction
        {
            Message = "Log message 4",
            CreatedAt = DateTime.UtcNow - AppInfo.DeleteServerLogsAfter - TimeSpan.FromSeconds(30),
        };
        await database.AdminActions.AddAsync(log4);

        await database.SaveChangesAsync();

        var countBefore = await database.AdminActions.CountAsync();

        var job = new DeleteOldAdminActionLogsJob(logger, database);
        await job.Execute(CancellationToken.None);

        Assert.NotNull(await ReadWithRawSql(log1.Id));
        Assert.NotNull(await ReadWithRawSql(log2.Id));
        Assert.Null(await ReadWithRawSql(log3.Id));
        Assert.NotNull(await ReadWithRawSql(log4.Id));
        Assert.Equal(countBefore - 1, await database.AdminActions.CountAsync());
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private Task<AdminAction?> ReadWithRawSql(long id)
    {
        // See the comments in SessionCleanupJobTests
        return fixture.Database.AdminActions
            .FromSqlInterpolated($"SELECT * FROM admin_actions WHERE id = {id}").FirstOrDefaultAsync();
    }
}
