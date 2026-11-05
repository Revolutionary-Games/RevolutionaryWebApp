namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class DownloadControllerTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("vip")]
    [InlineData("dev")]
    [InlineData("supporter")]
    public async Task PatronCreditsPreferAccountDisplayNames(string tier)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(nameof(PatronCreditsPreferAccountDisplayNames) + tier).Options;
        await using var database = new NotificationsEnabledDb(options,
            Substitute.For<IModelUpdateNotificationSender>());
        database.PatreonSettings.Add(new PatreonSettings { VipTierId = "vip", DevbuildsTierId = "dev" });
        database.Users.AddRange(
            new User("named@example.com", "AccountUsername") { DisplayName = "Alpha" },
            new User("null@example.com", "UnusedUsername"),
            new User("empty@example.com", "UnusedEmpty") { DisplayName = string.Empty },
            new User("blank@example.com", "UnusedBlank") { DisplayName = "  " });
        database.Patrons.AddRange(
            new Patron { Email = "named@example.com", Username = "Zulu", TierId = tier },
            new Patron { Email = "null@example.com", Username = "Bravo", TierId = tier },
            new Patron { Email = "empty@example.com", Username = "Charlie", TierId = tier },
            new Patron { Email = "blank@example.com", Username = "Delta", TierId = tier },
            new Patron { Email = "unmatched@example.com", Username = "Echo", TierId = tier },
            new Patron { Email = "suspended@example.com", Username = "Excluded", TierId = tier, Suspended = true });
        await database.SaveChangesAsync();

        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity("TestAuth")),
        };
        context.Items[AppInfo.CurrentUserMiddlewareKey] = new User("admin@example.com", "admin");
        var controller = new DownloadController(new XunitLogger<DownloadController>(output), database,
            Substitute.For<IGeneralRemoteDownloadUrls>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var response = await controller.DownloadPatronCredits();

        var credits = Assert.IsType<DevCenterCommunication.Models.PatreonCredits>(response.Value);
        var names = tier switch
        {
            "vip" => credits.VIPPatrons,
            "dev" => credits.DevBuildPatrons,
            _ => credits.SupporterPatrons,
        };
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo" }, names);
        Assert.Equal("Zulu", (await database.Patrons.FirstAsync(p => p.Email == "named@example.com")).Username);
    }
}
