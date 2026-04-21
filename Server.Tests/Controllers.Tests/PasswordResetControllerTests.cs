namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Utilities;
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
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class PasswordResetControllerTests : IDisposable
{
    private readonly XunitLogger<PasswordResetController> logger;
    private readonly ITestOutputHelper output;

    public PasswordResetControllerTests(ITestOutputHelper output)
    {
        this.output = output;
        logger = new XunitLogger<PasswordResetController>(output);
    }

    [Fact]
    public async Task PasswordReset_WorksAndAllowsLogin()
    {
        // Arrange: DB with a user
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);
        var user = new User("resetuser@example.com", "ResetUser")
        {
            NormalizedEmail = Normalization.NormalizeEmail("resetuser@example.com"),
            DisplayName = "Reset User",
            PasswordHash = Passwords.CreateSaltedPasswordHash("OldPassword123"),
            CreatedAt = DateTime.UtcNow,
            Local = true,
        };
        await database.Users.AddAsync(user);
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.SaveChangesAsync();

        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();

        var controller = CreateController(database, mailQueue, configuration);

        // Act 1: request forgot password
        var forgotResult = await controller.ForgotPassword(new ForgotPasswordRequest
        {
            Email = "resetuser@example.com",
            CSRF = string.Empty,
        });
        var forgotOk = Assert.IsType<OkObjectResult>(forgotResult);
        Assert.Equal(200, forgotOk.StatusCode);

        // Capture queued email and extract token from /reset-password/{token}
        await mailQueue.Received(1).SendEmail(Arg.Any<MailRequest>(), Arg.Any<CancellationToken>());
        var call = mailQueue.ReceivedCalls().First();
        var args = call.GetArguments();
        var mail = args[0] as MailRequest;
        Assert.NotNull(mail);
        Assert.Equal(EmailReason.PasswordReset, mail.Category);
        Assert.NotNull(mail.HtmlBody);

        var match = Regex.Match(mail.HtmlBody!, @"reset-password/([^""\s<]+)");
        Assert.True(match.Success, "Password reset token not found in email body");
        var token = match.Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(token));

        // Act 2: perform reset with new password
        var resetResponse = await controller.ResetPassword(new ResetPasswordRequest
        {
            Token = token,
            Password = "NewPassword456",
        });
        var resetOk = Assert.IsType<OkObjectResult>(resetResponse);
        Assert.Equal(200, resetOk.StatusCode);

        // Assert: login with new password works and sets session cookie
        var login = CreateLoginController(database, configuration);
        var loginResult = await login.PerformLocalLogin(new LoginFormData
        {
            CSRF = "csrf", // CSRF is not validated in tests for local login here
            Email = "resetuser@example.com",
            Password = "NewPassword456",
        });

        var redirect = Assert.IsType<RedirectResult>(loginResult);
        Assert.False(redirect.Permanent);

        // Assert a session was created and linked to the user
        var session = await database.Sessions.Include(s => s.User).FirstOrDefaultAsync();
        Assert.NotNull(session);
        Assert.NotNull(session.User);
        Assert.Equal(user.Id, session.UserId);
    }

    [Fact]
    public async Task PasswordReset_FailsOnInvalidToken()
    {
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        await using var database = CreateDb(notificationsMock);
        var mailQueue = Substitute.For<IMailQueue>();
        var configuration = BuildTestConfiguration();
        var controller = CreateController(database, mailQueue, configuration);

        var result = await controller.ResetPassword(new ResetPasswordRequest
        {
            Token = "this-is-not-a-valid-token",
            Password = "NewPassword456",
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
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
            ["Login:Local:Enabled"] = "true",
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
            .UseInMemoryDatabase("pwd-reset-" + Guid.NewGuid())
            .Options;
        return new NotificationsEnabledDb(options, notificationsMock);
    }

    private PasswordResetController CreateController(NotificationsEnabledDb database, IMailQueue mailQueue,
        IConfiguration configuration)
    {
        var dataProtection = new EphemeralDataProtectionProvider();
        var controller = new PasswordResetController(logger, database, mailQueue, configuration, dataProtection);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(),
        };
        return controller;
    }

    private LoginController CreateLoginController(NotificationsEnabledDb database, IConfiguration configuration)
    {
        var loginLogger = new XunitLogger<LoginController>(output);
        var csrf = Substitute.For<ITokenVerifier>();
        csrf.IsValidCSRFToken(Arg.Any<string>(), Arg.Any<User?>(), Arg.Any<bool>()).Returns(true);
        csrf.IsValidCSRFToken(Arg.Any<string>(), Arg.Any<User?>()).Returns(true);
        var redirectVerifier = new RedirectVerifier(configuration);
        var patreon = Substitute.For<IPatreonAPI>();
        var jobClient = Substitute.For<Hangfire.IBackgroundJobClient>();

        var login = new LoginController(loginLogger, database, configuration, csrf, redirectVerifier, patreon,
            jobClient);
        login.ControllerContext = new ControllerContext
        {
            HttpContext = CreateHttpContext(),
        };
        return login;
    }
}
