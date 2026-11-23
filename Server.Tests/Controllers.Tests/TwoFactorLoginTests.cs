namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Authorization;
using Server.Controllers;
using Server.Filters;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public sealed class TwoFactorLoginTests : IDisposable
{
    private readonly TwoFactorTestContext test = new();
    private readonly DefaultHttpContext context = new();
    private readonly ITokenVerifier csrf = Substitute.For<ITokenVerifier>();
    private readonly IPatreonAPI patreon = Substitute.For<IPatreonAPI>();
    private readonly ServiceProvider services;
    private readonly LoginController controller;

    public TwoFactorLoginTests()
    {
        services = new ServiceCollection().AddSingleton(test.Service).BuildServiceProvider();
        context.RequestServices = services;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        csrf.IsValidCSRFToken("csrf", Arg.Any<User?>(), true).Returns(true);
        controller = new LoginController(NullLogger<LoginController>.Instance, test.Database, test.Configuration,
            csrf, new RedirectVerifier(test.Configuration), patreon, test.Jobs)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    [Theory]
    [InlineData("local")]
    [InlineData(LoginController.SsoTypeGithub)]
    [InlineData(LoginController.SsoTypeCommunityForum)]
    [InlineData(LoginController.SsoTypeDevForum)]
    [InlineData(LoginController.SsoTypePatreon)]
    public async Task EveryLoginMethodRequiresTwoFactorBeforeAttachingUser(string method)
    {
        await test.Enable();
        context.Request.Headers.Cookie = $"{AppInfo.SessionCookieName}={test.Session.Id}:{test.User.Id}";
        var result = Assert.IsType<RedirectResult>(await BeginLogin(method));

        Assert.Equal("/login/two-factor", result.Url);
        AcceptSessionCookie();
        var pending = await test.Database.Sessions.Include(session => session.User).SingleAsync();
        Assert.NotEqual(test.Session.Id, pending.Id);
        Assert.Null(pending.UserId);
        Assert.Null(pending.User);
        Assert.Null(pending.CachedUserGroupsRaw);
        Assert.Null(pending.LastLoggedIn);
        Assert.Null(pending.TwoFactorVerifiedAt);
        Assert.Equal(DateTimeKind.Utc, pending.PendingTwoFactorExpiresAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, pending.LastUsed.Kind);
        Assert.Equal(test.User.Id, pending.PendingTwoFactorUserId);
        Assert.Null(pending.SsoNonce);
        Assert.Null(pending.StartedSsoLogin);
        Assert.Null(await UserFromCookiesHelper.GetUserFromSession(pending, test.Database));

        var rejected = Assert.IsType<RedirectResult>(await Complete("wrong"));
        Assert.StartsWith("/login/two-factor?error=", rejected.Url);
        Assert.Null(pending.UserId);

        var success = Assert.IsType<RedirectResult>(await Complete(test.BackupCodes[0]));
        Assert.Equal("http://localhost:5000/me", success.Url);
        var loggedIn = await test.Database.Sessions.SingleAsync();
        Assert.NotEqual(pending.Id, loggedIn.Id);
        Assert.Equal(test.User.Id, loggedIn.UserId);
        Assert.NotNull(loggedIn.LastLoggedIn);
        Assert.NotNull(loggedIn.TwoFactorVerifiedAt);
        Assert.Equal(DateTimeKind.Utc, loggedIn.TwoFactorVerifiedAt.Value.Kind);
        Assert.Null(loggedIn.PendingTwoFactorUserId);
        Assert.Null(loggedIn.PendingTwoFactorReturnUrl);

        Assert.StartsWith("/login?error=", Assert.IsType<RedirectResult>(await Complete(test.BackupCodes[1])).Url);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("suspended")]
    [InlineData("new-key")]
    [InlineData("disabled")]
    public async Task ChallengeRejectsStaleOrInvalidPendingLogins(string state)
    {
        await test.Enable();
        await BeginLogin("local");
        AcceptSessionCookie();
        switch (state)
        {
            case "expired":
                test.Clock.Advance(TimeSpan.FromMinutes(11));
                break;
            case "suspended":
                test.User.SuspendedUntil = DateTime.UtcNow.AddDays(1);
                break;
            case "new-key":
                test.Credential.KeyVersion = Guid.NewGuid();
                break;
            case "disabled":
                test.Credential.Enabled = false;
                break;
        }

        await test.Database.SaveChangesAsync();
        Assert.IsType<UnauthorizedObjectResult>((await controller.GetTwoFactorChallenge(test.Service)).Result);
        Assert.StartsWith("/login?error=", Assert.IsType<RedirectResult>(await Complete(test.BackupCodes[0])).Url);
        Assert.All(await test.Database.Sessions.Where(s => s.PendingTwoFactorUserId != null).ToListAsync(),
            session => Assert.Null(session.UserId));
        Assert.Equal(10, TwoFactorService.GetBackupCodeHashes(test.Credential).Count);
    }

    [Fact]
    public async Task RecoveryRequiresFirstFactorAndChallengeCsrf()
    {
        await test.Enable();
        var request = new TwoFactorRecoveryFormData { CSRF = "csrf" };
        Assert.StartsWith("/login?error=",
            Assert.IsType<RedirectResult>(await controller.RequestTwoFactorRecovery(request, test.Service)).Url);
        Assert.Null(test.Credential.RecoveryRequestedAt);

        await BeginLogin("local");
        AcceptSessionCookie();
        await Assert.ThrowsAsync<HttpResponseException>(() => controller.RequestTwoFactorRecovery(
            new TwoFactorRecoveryFormData { CSRF = "bad-csrf" }, test.Service));
        Assert.Null(test.Credential.RecoveryRequestedAt);

        Assert.Equal("/login/two-factor",
            Assert.IsType<RedirectResult>(await controller.RequestTwoFactorRecovery(request, test.Service)).Url);
        Assert.NotNull(test.Credential.RecoveryRequestedAt);
        Assert.Null((await test.Database.Sessions.SingleAsync(s => s.PendingTwoFactorUserId != null)).UserId);

        await Complete(test.BackupCodes[0]);
        Assert.Null(test.Credential.RecoveryRequestedAt);
    }

    [Fact]
    public async Task ApiCredentialsCannotManageTwoFactorWithoutBrowserSession()
    {
        await test.Enable();
        context.User = new GenericPrincipal(test.User, []);
        context.Items[AppInfo.CurrentUserMiddlewareKey] = test.User;
        using var settings = new TwoFactorController(test.Service);
        settings.ControllerContext = new ControllerContext { HttpContext = context };
        var code = new TwoFactorCodeForm { Code = test.BackupCodes[0] };

        Assert.IsType<UnauthorizedObjectResult>((await settings.Get()).Result);
        Assert.IsType<UnauthorizedObjectResult>((await settings.Setup()).Result);
        Assert.IsType<UnauthorizedObjectResult>((await settings.Enable(code)).Result);
        Assert.IsType<UnauthorizedObjectResult>(await settings.Disable(code));
        Assert.IsType<UnauthorizedObjectResult>((await settings.RegenerateBackupCodes(code)).Result);
        Assert.True(test.Credential.Enabled);
        Assert.Equal(10, TwoFactorService.GetBackupCodeHashes(test.Credential).Count);
    }

    [Fact]
    public async Task LoginChallengeNeverRedirectsToExternalReturnUrl()
    {
        await test.Enable();
        await controller.PerformLocalLogin(new LoginFormData
        {
            Email = test.User.Email,
            Password = "TestPassword123",
            CSRF = "csrf",
            ReturnUrl = "https://attacker.example.com/",
        });
        AcceptSessionCookie();

        Assert.Equal("/", Assert.IsType<RedirectResult>(await Complete(test.BackupCodes[0])).Url);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminCodeCannotBeConsumedBeforeTwoFactorIsEnabled(bool enabled)
    {
        if (enabled)
            await test.Enable();

        var code = new RedeemableCode { GrantedResource = "GroupAdmin" };
        test.Database.RedeemableCodes.Add(code);
        await test.Database.SaveChangesAsync();
        context.User = new GenericPrincipal(test.User, []);
        context.Items[AppInfo.CurrentUserMiddlewareKey] = test.User;
        var redeem = new CodeRedeemController(NullLogger<CodeRedeemController>.Instance, test.Database, test.Jobs)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await redeem.Redeem(new RedeemCodeData { Code = code.Id.ToString() });

        if (enabled)
        {
            Assert.IsType<OkObjectResult>(result);
            Assert.Contains(test.User.Groups, g => g.Id == GroupType.Admin);
            Assert.Empty(test.Database.RedeemableCodes);
        }
        else
        {
            Assert.Contains("two-factor", Assert.IsType<BadRequestObjectResult>(result).Value!.ToString());
            Assert.DoesNotContain(test.User.Groups, g => g.Id == GroupType.Admin);
            Assert.Single(test.Database.RedeemableCodes);
        }
    }

    public void Dispose()
    {
        controller.Dispose();
        test.Dispose();
        services.Dispose();
    }

    private Task<IActionResult> Complete(string code)
    {
        return controller.CompleteTwoFactorLogin(new TwoFactorLoginFormData { CSRF = "csrf", Code = code },
            test.Service);
    }

    private void AcceptSessionCookie()
    {
        context.Request.Headers.Cookie = context.Response.Headers.SetCookie.Last()!.Split(';')[0];
        context.Response.Headers.Clear();
    }

    private async Task<IActionResult> BeginLogin(string method)
    {
        if (method == "local")
        {
            return await controller.PerformLocalLogin(new LoginFormData
            {
                Email = test.User.Email,
                Password = "TestPassword123",
                CSRF = "csrf",
                ReturnUrl = "http://localhost:5000/me",
            });
        }

        test.User.SsoSource = method;
        if (method == LoginController.SsoTypePatreon)
            test.User.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User, GroupType.PatreonSupporter));
        await controller.StartSsoLogin(new SsoStartFormData
        {
            CSRF = "csrf",
            SsoType = method,
            ReturnUrl = "http://localhost:5000/me",
        });
        AcceptSessionCookie();
        var session = await test.Database.Sessions.SingleAsync(s => s.StartedSsoLogin == method);

        if (method == LoginController.SsoTypeGithub)
        {
            var github = Substitute.For<IGithubOAuthClient>();
            github.GetUser(Arg.Any<string>(), Arg.Any<string>()).Returns((new GithubUserInfo
            {
                Login = test.User.UserName,
            }, new List<GithubEmail> { new() { Email = test.User.Email, Primary = true, Verified = true } }));
            return await controller.SsoReturnGithub(session.SsoNonce!, "github-code", null, github);
        }

        if (method == LoginController.SsoTypePatreon)
        {
            test.Database.Patrons.Add(new Patron
            {
                Email = test.User.Email,
                Username = test.User.UserName,
                TierId = "devbuilds",
            });
            test.Database.PatreonSettings.Add(new PatreonSettings { DevbuildsTierId = "devbuilds" });
            await test.Database.SaveChangesAsync();
            patreon.TurnCodeIntoTokens(Arg.Any<string>(), Arg.Any<string>()).Returns(new PatreonAPIBearerToken());
            patreon.GetOwnDetails().Returns(new PatreonAPIObjectResponse
            {
                Data = new PatreonObjectData
                {
                    Attributes = new PatreonObjectAttributes { Email = test.User.Email },
                },
            });
            return await controller.SsoReturnPatreon(session.SsoNonce!, "patreon-code", null);
        }

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"nonce={session.SsoNonce}&email={test.User.Email}&username={test.User.UserName}"));
        var secret = method == LoginController.SsoTypeDevForum ? "developer-secret" : "forum-secret";
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return method == LoginController.SsoTypeDevForum ?
            await controller.SsoReturnDev(payload, signature) :
            await controller.SsoReturnCommunity(payload, signature);
    }
}
