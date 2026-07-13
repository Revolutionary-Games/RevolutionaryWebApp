namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
using RevolutionaryWebApp.Server.Authorization;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
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
                m != null && m.Recipient == "user@example.com" && m.Category == EmailReason.ConfirmEmail &&
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

        var ok = Assert.IsType<OkResult>(response);
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

    [Fact]
    public async Task EndToEnd_SignupSetsSessionCookieOnSuccess()
    {
        // Arrange
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken("valid", null, false).Returns(true);

        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);

        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();
        var jobClient = Substitute.For<IBackgroundJobClient>();

        var controller = CreateController(database, csrfMock, mailQueue, configuration, jobClient);

        // Act 1: start signup
        var startResult = await controller.Start(new SignupStartRequest { CSRF = "valid", Email = "e2e@example.com" });
        Assert.IsType<OkResult>(startResult);

        // Capture the token from the queued email body
        await mailQueue.Received(1).SendEmail(Arg.Any<MailRequest>(), Arg.Any<CancellationToken>());
        var firstCall = mailQueue.ReceivedCalls().FirstOrDefault();
        Assert.NotNull(firstCall);
        var args = firstCall.GetArguments();
        var sent = args[0] as MailRequest;
        Assert.NotNull(sent);
        Assert.Equal(EmailReason.ConfirmEmail, sent.Category);
        Assert.NotNull(sent.HtmlBody);

        var match = Regex.Match(sent.HtmlBody, "complete-signup/([A-Za-z0-9]+)");
        Assert.True(match.Success, "Signup completion token not found in email body");
        var token = match.Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(token));

        // Act 2: complete signup
        var completeResponse = await controller.Complete(new SignupCompleteRequest
        {
            CSRF = "valid",
            Token = token,
            UserName = "E2EUser",
            DisplayName = "Just some guy",
            Password = "StrongPassword123",
        });

        var ok = Assert.IsType<OkResult>(completeResponse);
        Assert.Equal(200, ok.StatusCode);

        // Assert user exists
        var user = await database.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == "e2e@example.com");
        Assert.NotNull(user);
        Assert.Equal("Just some guy", user.DisplayName);

        // Assert Set-Cookie header contains a session cookie
        var setCookieHeader = controller.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(AppInfo.SessionCookieName, setCookieHeader);
        Assert.False(string.IsNullOrWhiteSpace(setCookieHeader));

        // Extract cookie value (before first ';') and verify it authenticates in a new request context
        var cookieNameEq = AppInfo.SessionCookieName + "=";
        var startIndex = setCookieHeader.IndexOf(cookieNameEq, StringComparison.Ordinal);
        Assert.True(startIndex >= 0);
        startIndex += cookieNameEq.Length;
        var endIndex = setCookieHeader.IndexOf(';', startIndex);
        var cookieValue = endIndex >= 0 ?
            setCookieHeader.Substring(startIndex, endIndex - startIndex) :
            setCookieHeader.Substring(startIndex);
        Assert.False(string.IsNullOrWhiteSpace(cookieValue));

        var ctx = CreateHttpContext();
        ctx.Request.Headers["Cookie"] = AppInfo.SessionCookieName + "=" + cookieValue;

        var (resolvedUser, session) =
            await ctx.Request.Cookies.GetUserFromSession(database, ctx.Connection.RemoteIpAddress);
        Assert.NotNull(resolvedUser);
        Assert.Equal(user.Id, resolvedUser.Id);
        Assert.NotNull(session);
        Assert.Equal(user.Id, session.UserId);
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
            ["Registration:Enabled"] = "true",
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
