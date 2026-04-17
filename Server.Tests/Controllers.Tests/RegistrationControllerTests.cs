namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Utilities;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared.Forms;
using Shared.Models.Enums;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class RegistrationControllerTests : IDisposable
{
    private readonly XunitLogger<SignupsController> logger;

    public RegistrationControllerTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<SignupsController>(output);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad")]
    public async Task SignupStart_FailsOnInvalidCSRF(string? csrf)
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(Arg.Any<string>(), null, false).Returns(false);

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);

        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();
        var jobClient = Substitute.For<IBackgroundJobClient>();

        var controller = CreateController(database, csrfMock, mailQueue, configuration, jobClient);

        var result = await controller.Start(new SignupStartRequest { CSRF = csrf!, Email = "user@example.com" });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, bad.StatusCode);
        await mailQueue.DidNotReceiveWithAnyArgs().SendEmail(null!, CancellationToken.None);
    }

    [Fact]
    public async Task SignupStart_QueuesEmailAndCreatesPending()
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken("valid", null, false).Returns(true);

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);

        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();
        var jobClient = Substitute.For<IBackgroundJobClient>();

        var controller = CreateController(database, csrfMock, mailQueue, configuration, jobClient);

        var result = await controller.Start(new SignupStartRequest { CSRF = "valid", Email = "user@example.com" });

        Assert.IsType<OkResult>(result);

        // DB pending created
        var pending = await database.PendingUserSignups.AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(pending);
        Assert.Equal("user@example.com", pending.Email);
        Assert.False(string.IsNullOrWhiteSpace(pending.Token));

        // Email queued
        await mailQueue.Received(1).SendEmail(Arg.Is<MailRequest>(m =>
                m.Recipient == "user@example.com" && m.Category == EmailReason.ConfirmEmail &&
                m.HtmlBody != null && m.HtmlBody.Contains("complete-signup/", StringComparison.OrdinalIgnoreCase)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SignupStart_RateLimitsAfterFewAttempts()
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken("valid", null, false).Returns(true);

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);

        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();
        var jobClient = Substitute.For<IBackgroundJobClient>();

        var controller = CreateController(database, csrfMock, mailQueue, configuration, jobClient);

        // First three attempts should be allowed immediately
        for (var i = 0; i < 3; ++i)
        {
            var result = await controller.Start(new SignupStartRequest { CSRF = "valid", Email = "limit@test.com" });
            Assert.IsType<OkResult>(result);
        }

        var rateLimited = await database.PendingUserSignups.AsNoTracking().FirstAsync();
        Assert.True(rateLimited.SendCount < 4);

        // Fourth within 1 hour should be 429
        var blocked = await controller.Start(new SignupStartRequest { CSRF = "valid", Email = "limit@test.com" });
        var tooMany = Assert.IsType<ObjectResult>(blocked);
        Assert.Equal(StatusCodes.Status429TooManyRequests, tooMany.StatusCode);

        // Advance last sent beyond 1 hour and try again
        var pending = await database.PendingUserSignups.FirstAsync();
        pending.LastEmailSentUtc = DateTime.UtcNow.Subtract(TimeSpan.FromHours(2));
        await database.SaveChangesAsync();

        var allowedAgain = await controller.Start(new SignupStartRequest { CSRF = "valid", Email = "limit@test.com" });
        Assert.IsType<OkResult>(allowedAgain);

        // Verify send count progressed
        var reloaded = await database.PendingUserSignups.AsNoTracking().FirstAsync();
        Assert.True(reloaded.SendCount >= 4);
    }

    [Fact]
    public async Task GetPending_ReturnsEmail()
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);
        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();
        var jobClient = Substitute.For<IBackgroundJobClient>();

        // Seed a pending signup
        var pending = new PendingUserSignup
        {
            Email = "person@test.invalid",
            NormalizedEmail = Normalization.NormalizeEmail("person@test.invalid"),
            Token = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
        };
        await database.PendingUserSignups.AddAsync(pending);
        await database.SaveChangesAsync();

        var controller = CreateController(database, csrfMock, mailQueue, configuration, jobClient);

        var result = await controller.GetPending(pending.Token);
        var ok = Assert.IsType<ActionResult<PendingSignupInfoDTO>>(result);
        var dto = Assert.IsType<PendingSignupInfoDTO>(ok.Value);
        Assert.Equal("person@test.invalid", dto.Email);
    }

    [Fact]
    public async Task Complete_CreatesUserDeletesPending()
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken("csrf", null, false).Returns(true);
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);
        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();
        var jobClient = Substitute.For<IBackgroundJobClient>();

        // Seed pending
        var token = Guid.NewGuid().ToString("N");
        await database.PendingUserSignups.AddAsync(new PendingUserSignup
        {
            Email = "newuser@example.com",
            NormalizedEmail = Normalization.NormalizeEmail("newuser@example.com"),
            Token = token,
            CreatedUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync();

        var controller = CreateController(database, csrfMock, mailQueue, configuration, jobClient);

        var response = await controller.Complete(new SignupCompleteRequest
        {
            CSRF = "csrf",
            Token = token,
            UserName = "NewUser",
            DisplayName = "The New User",
            Password = "StrongPassword123",
        });

        var ok = Assert.IsType<OkObjectResult>(response);
        Assert.Equal(200, ok.StatusCode);

        // User created
        var user = await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == "newuser@example.com");
        Assert.NotNull(user);
        Assert.False(string.IsNullOrEmpty(user.PasswordHash));

        // Pending removed
        var stillPending = await database.PendingUserSignups.AsNoTracking().AnyAsync();
        Assert.False(stillPending);
    }

    [Fact]
    public async Task Complete_FailsOnShortPassword()
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken("csrf", null, false).Returns(true);
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);
        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();
        var jobClient = Substitute.For<IBackgroundJobClient>();

        // Seed pending
        var token = Guid.NewGuid().ToString("N");
        await database.PendingUserSignups.AddAsync(new PendingUserSignup
        {
            Email = "shortpass@example.com",
            NormalizedEmail = Normalization.NormalizeEmail("shortpass@example.com"),
            Token = token,
            CreatedUtc = DateTime.UtcNow,
        });
        await database.SaveChangesAsync();

        var controller = CreateController(database, csrfMock, mailQueue, configuration, jobClient);

        var response = await controller.Complete(new SignupCompleteRequest
        {
            CSRF = "csrf",
            Token = token,
            UserName = "UserShort",
            DisplayName = null,
            Password = "123", // too short
        });

        var bad = Assert.IsType<BadRequestObjectResult>(response);
        Assert.Equal(400, bad.StatusCode);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static IConfiguration BuildTestConfiguration()
    {
        var dict = new Dictionary<string, string?>
        {
            ["BaseUrl"] = "https://example.test/",
            ["UseSecureCookies"] = "false",
        };

        return new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
    }

    private static DefaultHttpContext CreateHttpContext()
    {
        var context = new DefaultHttpContext
        {
            Connection =
            {
                RemoteIpAddress = System.Net.IPAddress.Loopback,
            },
        };
        return context;
    }

    private static NotificationsEnabledDb CreateDb(IModelUpdateNotificationSender notificationsMock)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("signup-" + Guid.NewGuid())
            .Options;
        return new NotificationsEnabledDb(options, notificationsMock);
    }

    private SignupsController CreateController(NotificationsEnabledDb database, ITokenVerifier csrf,
        IMailQueue mailQueue, IConfiguration configuration, IBackgroundJobClient jobClient)
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var controller = new SignupsController(logger, database, csrf, mailQueue,
            configuration, dataProtection, jobClient);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(),
        };
        return controller;
    }
}
