namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class PatreonConfigurationControllerTests
{
    private readonly ITestOutputHelper output;

    public PatreonConfigurationControllerTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public async Task PatreonConfigurationController_UpdateUpdatesWebhookInfo()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("PatreonUpdateTest").Options;
        var db = new NotificationsEnabledDb(options, notificationsMock);

        var settings = new PatreonSettings
        {
            Id = 1,
            CreatorToken = "old_token",
            WebhookId = "old_webhook_id",
            WebhookSecret = "old_webhook_secret",
        };
        await db.PatreonSettings.AddAsync(settings);

        var user = new User("admin@example.com", "admin")
        {
            Id = 1,
        };
        await db.Users.AddAsync(user);
        await db.SaveChangesAsync();

        var logger = new XunitLogger<PatreonConfigurationController>(output);
        var controller = new PatreonConfigurationController(logger, db);

        var httpContext = new DefaultHttpContext();
        httpContext.Items[AppInfo.CurrentUserMiddlewareKey] = user;
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };

        var request = new PatreonSettingsDTO
        {
            Id = 1,
            Active = true,
            CreatorToken = "new_token",
            WebhookId = "new_webhook_id",
            WebhookSecret = "new_webhook_secret",
        };

        var result = await controller.Update(1, request);

        Assert.IsType<OkResult>(result);

        var updatedSettings = await db.PatreonSettings.FindAsync(1L);
        Assert.NotNull(updatedSettings);
        Assert.Equal("new_token", updatedSettings.CreatorToken);
        Assert.Equal("new_webhook_id", updatedSettings.WebhookId);
        Assert.Equal("new_webhook_secret", updatedSettings.WebhookSecret);
    }
}
