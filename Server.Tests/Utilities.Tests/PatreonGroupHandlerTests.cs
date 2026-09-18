namespace RevolutionaryWebApp.Server.Tests.Utilities.Tests;

using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Models;
using Server.Services;
using Server.Utilities;
using Shared.Models;
using Xunit;

public sealed class PatreonGroupHandlerTests
{
    [Fact]
    public async Task MemberWithPrivateEmailGetsStableSyntheticEmailAndPatreonIds()
    {
        var notifications = Substitute.For<IModelUpdateNotificationSender>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(MemberWithPrivateEmailGetsStableSyntheticEmailAndPatreonIds)).Options;
        await using var database = new NotificationsEnabledDb(options, notifications);

        var member = new PatreonObjectData
        {
            Id = "member-1",
            Type = "member",
            Attributes = new PatreonObjectAttributes
            {
                FullName = "Private Patron",
                PatronStatus = "active_patron",
                CurrentlyEntitledAmountCents = 500,
            },
        };
        var user = new PatreonObjectData
        {
            Id = "user-1",
            Type = "user",
            Attributes = new PatreonObjectAttributes { FullName = "Private Patron" },
        };
        var tier = new PatreonObjectData { Id = "tier-1", Type = "tier" };

        await PatreonGroupHandler.HandlePatreonMemberObject(member, user, [tier], database,
            Substitute.For<IBackgroundJobClient>());
        await database.SaveChangesAsync();

        var patron = await database.Patrons.SingleAsync();
        Assert.True(PatreonGroupHandler.IsSyntheticEmail(patron.Email));
        Assert.Equal("member-1", patron.PatreonMemberId);
        Assert.Equal("user-1", patron.PatreonUserId);
        Assert.Equal("tier-1", patron.EntitledTierIds);
    }

    [Fact]
    public void SettingsRecognizeAnyCurrentlyEntitledTier()
    {
        var settings = new PatreonSettings
        {
            VipTierId = "vip",
            DevbuildsTierId = "dev",
        };
        var patron = new Patron
        {
            Email = "patron@example.com",
            Username = "Patron",
            TierId = "other",
            EntitledTierIds = "other,vip,dev",
        };

        Assert.True(settings.IsEntitledToVIP(patron));
        Assert.True(settings.IsEntitledToDevBuilds(patron));
    }
}
