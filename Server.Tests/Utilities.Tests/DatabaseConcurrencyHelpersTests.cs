namespace ThriveDevCenter.Server.Tests.Utilities.Tests;

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Utilities;
using Xunit;

public class DatabaseConcurrencyHelpersTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private const string UpdatedDescription = "Updated description";

    private readonly RealUnitTestDatabaseFixture fixture;

    public DatabaseConcurrencyHelpersTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task SaveConcurrencyErrorResolving_WorksWithCrashReportApproach()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();

        var item1 = new CrashReport
        {
            Description = "Test description",
            DumpLocalFileName = "test.dmp",
            UploadedFrom = new IPAddress(1234),
            ExitCodeOrSignal = "SIGSEGV",
            Logs = "logs",
        };

        await database.CrashReports.AddAsync(item1);
        await database.SaveChangesAsync();

        var item1Instance2 = await database.CrashReports.FindAsync(item1.Id);
        Assert.NotNull(item1Instance2);
        Assert.Equal(item1.Id, item1Instance2.Id);

        Assert.NotEqual(UpdatedDescription, item1.Description);
        Assert.Equal(item1.Description, item1Instance2.Description);
        item1Instance2.Description = UpdatedDescription;
        Assert.NotNull(item1Instance2.DumpLocalFileName);

        // Intentionally cause a conflict
        await fixture.Database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE crash_reports SET dump_local_file_name = NULL WHERE id = {item1.Id}");

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => database.SaveChangesAsync());

        Assert.NotNull(item1Instance2.DumpLocalFileName);

        await database.SaveChangesWithConflictResolvingAsync(
            conflictEntries =>
            {
                Assert.Equal(UpdatedDescription, item1Instance2.Description);
                DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflictEntries, item1Instance2);
                Assert.NotEqual(UpdatedDescription, item1Instance2.Description);
                item1Instance2.Description = UpdatedDescription;
            }, CancellationToken.None, false);

        Assert.Equal(UpdatedDescription, item1Instance2.Description);

        Assert.Null(item1Instance2.DumpLocalFileName);
    }
}