namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Xunit;

public class PurgeOldModerationRevisionsJobTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;

    public PurgeOldModerationRevisionsJobTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task DeletesExpiredRevisionsAndKeepsRecentOnes()
    {
        var database = fixture.Database;
        await using var transaction = await database.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var expired = new ModerationRevision { CreatedAt = now.AddMonths(-12).AddDays(-1), RevertedAt = now };
        var recent = new ModerationRevision { CreatedAt = now.AddMonths(-12).AddDays(1) };
        database.ModerationRevisions.AddRange(expired, recent);
        await database.SaveChangesAsync();

        await new PurgeOldModerationRevisionsJob(database).Execute(CancellationToken.None);

        var remaining = await database.ModerationRevisions.Where(revision =>
            revision.Id == expired.Id || revision.Id == recent.Id).Select(revision => revision.Id).ToListAsync();
        Assert.Equal(recent.Id, Assert.Single(remaining));
    }
}
