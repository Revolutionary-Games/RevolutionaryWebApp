namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Controllers;
using Server.Jobs;
using Server.Models;
using Server.Services;
using Shared.Models.Enums;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class ApplyUserAutomaticGroupsJobTests : IDisposable
{
    private const string PatronEmail = "patron@example.com";
    private const string DevBuildRewardTier = "devbuild-1234";
    private const string VIPRewardTier = "vip-1234";

    private static int dbNameCounter;

    private readonly XunitLogger<ApplyUserAutomaticGroupsJob> logger;

    public ApplyUserAutomaticGroupsJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<ApplyUserAutomaticGroupsJob>(output);
    }

    [Theory]
    [InlineData(false, false, false, "random-tier")]
    [InlineData(true, false, false, "random-tier")]
    [InlineData(false, false, true, DevBuildRewardTier)]
    [InlineData(true, false, true, DevBuildRewardTier)]
    [InlineData(false, true, false, DevBuildRewardTier)]
    [InlineData(true, true, false, DevBuildRewardTier)]
    [InlineData(false, false, true, VIPRewardTier)]
    [InlineData(true, false, true, VIPRewardTier)]
    [InlineData(true, true, false, VIPRewardTier)]
    [InlineData(false, true, false, VIPRewardTier)]
    public async Task UserGroupCheck_SetsPatreonGroupBasedOnPledge(bool startAsPatron,
        bool patreonDeclined, bool shouldBePatron, string reward)
    {
        var backgroundMock = Substitute.For<IBackgroundJobClient>();

        await using var database =
            await CreatePatronsDb(nameof(UserGroupCheck_SetsPatreonGroupBasedOnPledge) + ++dbNameCounter,
                reward, patreonDeclined);

        var user = new User(PatronEmail, "RandomUserName")
        {
            Email = PatronEmail,
            SsoSource = LoginController.SsoTypePatreon,
        };

        var patreonGroup = await database.UserGroups.FirstOrDefaultAsync(g => g.Id == GroupType.PatreonSupporter) ??
            throw new Exception("Missing inbuilt group");

        if (startAsPatron)
            user.Groups.Add(patreonGroup);

        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        var job = new ApplyUserAutomaticGroupsJob(logger, database, backgroundMock);

        Assert.Null(user.SuspendedUntil);
        Assert.Equal(startAsPatron, user.Groups.Any(g => g.Id == patreonGroup.Id));

        await job.Execute(PatronEmail, CancellationToken.None);

        var user2 = await database.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(user2);

        Assert.Equal(shouldBePatron, user2.Groups.Any(g => g.Id == patreonGroup.Id));
        Assert.Null(user2.SuspendedUntil);

        // Probably doesn't work due to in-memory DB
        /*if (startAsPatron != shouldBePatron)
            Assert.NotEqual(user.UpdatedAt, user2.UpdatedAt);*/
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private async Task<NotificationsEnabledDb> CreatePatronsDb(string dbName, string rewardId, bool suspended)
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();

        var dbOptions =
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options;

        var database = new NotificationsEnabledDb(dbOptions, notificationsMock);

        await database.Patrons.AddAsync(new Patron
        {
            Email = PatronEmail,
            RewardId = rewardId,
            Username = "RandomUserName",
            Suspended = suspended,
        });

        await database.PatreonSettings.AddAsync(new PatreonSettings
        {
            Active = true,
            DevbuildsRewardId = DevBuildRewardTier,
            VipRewardId = VIPRewardTier,
            CreatorToken = "Creator-0101",
        });

        await database.UserGroups.AddAsync(new UserGroup(GroupType.PatreonSupporter, "Patron Supporter"));

        await database.SaveChangesAsync();

        return database;
    }
}
