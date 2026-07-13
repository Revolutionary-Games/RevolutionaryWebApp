namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System.Net.Http;
using System.Security.Claims;
using System.Threading;
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
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("PatreonUpdateTest")
            .Options;
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
        var patreonCreatorMock = Substitute.For<IPatreonCreatorAPI>();
        var httpClientFactoryMock = Substitute.For<IHttpClientFactory>();
        var controller = new PatreonConfigurationController(logger, db, patreonCreatorMock, httpClientFactoryMock);

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

    [Fact]
    public async Task PatreonConfigurationController_VerifyTokenUsesStoredTokenWhenEmpty()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("PatreonVerifyTest")
            .Options;
        var db = new NotificationsEnabledDb(options, notificationsMock);

        var settings = new PatreonSettings
        {
            Id = 1,
            CreatorToken = "stored_token",
            WebhookId = "id",
            WebhookSecret = "secret",
        };
        await db.PatreonSettings.AddAsync(settings);
        await db.SaveChangesAsync();

        var logger = new XunitLogger<PatreonConfigurationController>(output);
        var patreonCreatorMock = Substitute.For<IPatreonCreatorAPI>();
        var httpClientFactoryMock = Substitute.For<IHttpClientFactory>();
        httpClientFactoryMock.CreateClient(Arg.Any<string>()).Returns(new HttpClient());

        patreonCreatorMock.GetOwnDetails(Arg.Any<HttpClient>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PatreonAPIObjectResponse
            {
                Data = new PatreonObjectData
                {
                    Attributes = new PatreonObjectAttributes
                    {
                        FullName = "Test User",
                        Email = "test@example.com",
                    },
                },
            });

        var controller = new PatreonConfigurationController(logger, db, patreonCreatorMock, httpClientFactoryMock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var request = new RemotePatreonRequest
        {
            Id = 1,
            Token = null, // Empty token should trigger lookup
        };

        var result = await controller.VerifyToken(request);

        if (result.Result is BadRequestObjectResult badRequest)
            Assert.Fail("Verification failed: " + badRequest.Value);

        Assert.Equal("Token is valid. Authenticated as: Test User (test@example.com)", result.Value);

        // Verify that the client was configured and token was passed
        httpClientFactoryMock.Received().CreateClient(Arg.Any<string>());
        await patreonCreatorMock.Received().GetOwnDetails(Arg.Any<HttpClient>(),
            Arg.Is("stored_token"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PatreonConfigurationController_CreateWorksWhenEmpty()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("PatreonCreateTest")
            .Options;
        var db = new NotificationsEnabledDb(options, notificationsMock);

        var user = new User("admin@example.com", "admin")
        {
            Id = 1,
        };

        var httpContext = new DefaultHttpContext();
        httpContext.Items[AppInfo.CurrentUserMiddlewareKey] = user;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, user.Email),
        }, "TestAuth"));

        var logger = new XunitLogger<PatreonConfigurationController>(output);
        var patreonCreatorMock = Substitute.For<IPatreonCreatorAPI>();
        var httpClientFactoryMock = Substitute.For<IHttpClientFactory>();
        var controller = new PatreonConfigurationController(logger, db, patreonCreatorMock, httpClientFactoryMock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext,
            },
        };

        // Ensure initially empty
        Assert.Empty(await db.PatreonSettings.ToListAsync());

        var result = await controller.Create();
        Assert.IsType<OkResult>(result);

        var settings = await db.PatreonSettings.SingleAsync();
        Assert.False(settings.Active);
        Assert.Equal(string.Empty, settings.CreatorToken);
        Assert.Equal(string.Empty, settings.WebhookId);
        Assert.Equal(string.Empty, settings.WebhookSecret);

        // Verify AdminAction was created
        var adminAction = await db.AdminActions.SingleAsync();
        Assert.Equal("Patreon settings created", adminAction.Message);
        Assert.Equal(user.Id, adminAction.PerformedById);
    }

    [Fact]
    public async Task PatreonConfigurationController_CreateFailsWhenNotEmpty()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase("PatreonCreateFailTest")
            .Options;
        var db = new NotificationsEnabledDb(options, notificationsMock);

        await db.PatreonSettings.AddAsync(new PatreonSettings
        {
            CreatorToken = "existing",
        });
        await db.SaveChangesAsync();

        var logger = new XunitLogger<PatreonConfigurationController>(output);
        var patreonCreatorMock = Substitute.For<IPatreonCreatorAPI>();
        var httpClientFactoryMock = Substitute.For<IHttpClientFactory>();
        var controller = new PatreonConfigurationController(logger, db, patreonCreatorMock, httpClientFactoryMock);

        var result = await controller.Create();
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Patreon settings already exist", badRequest.Value);
    }
}
