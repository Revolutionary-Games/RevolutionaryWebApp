namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Controllers;
using Server.Jobs;
using Server.Models;
using Server.Services;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class CheckSSOUserSuspensionJobTests : System.IDisposable
{
    private const string PatronEmail = "patron@example.com";
    private const string DevBuildRewardTier = "devbuild-1234";
    private const string VIPRewardTier = "vip-1234";

    private static int dbNameCounter = 0;

    private readonly XunitLogger<CheckSSOUserSuspensionJob> logger;

    public CheckSSOUserSuspensionJobTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<CheckSSOUserSuspensionJob>(output);
    }

    [Theory]
    [InlineData(false, false, true, "random-tier")]
    [InlineData(true, false, true, "random-tier")]
    [InlineData(false, false, false, DevBuildRewardTier)]
    [InlineData(true, false, false, DevBuildRewardTier)]
    [InlineData(false, true, true, DevBuildRewardTier)]
    [InlineData(true, true, true, DevBuildRewardTier)]
    [InlineData(false, false, false, VIPRewardTier)]
    [InlineData(true, false, false, VIPRewardTier)]
    [InlineData(true, true, true, VIPRewardTier)]
    [InlineData(false, true, true, VIPRewardTier)]
    public async Task SSOSuspensionCheck_SuspendsBasedOnPatreonPledgeLevel(bool startSuspended,
        bool patreonDeclined, bool shouldBeSuspended, string reward)
    {
        var communityDiscourseMock = Substitute.For<ICommunityForumAPI>();
        var devDiscourseMock = Substitute.For<IDevForumAPI>();

        await using var database =
            await CreatePatronsDb(nameof(SSOSuspensionCheck_SuspendsBasedOnPatreonPledgeLevel) + ++dbNameCounter,
                reward,
                patreonDeclined);

        var user = new User(PatronEmail, "RandomUserName")
        {
            Email = PatronEmail,
            Suspended = startSuspended,
            SsoSource = LoginController.SsoTypePatreon,
        };

        if (startSuspended)
            user.SuspendedReason = "Test reason";

        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        var job = new CheckSSOUserSuspensionJob(logger, database, communityDiscourseMock,
            devDiscourseMock);

        if (!startSuspended)
            Assert.Null(user.SuspendedReason);
        Assert.Equal(startSuspended, user.Suspended);

        await job.Execute(PatronEmail, CancellationToken.None);

        Assert.Equal(shouldBeSuspended, user.Suspended);

        if (shouldBeSuspended)
        {
            Assert.NotNull(user.SuspendedReason);
        }
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

        await database.SaveChangesAsync();

        return database;
    }
}
