namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public sealed class GithubLoginTests : IDisposable
{
    private const string CallbackUrl = "http://localhost:5000/OAuthReturn/github";
    private const string UserEmail = "github@example.com";

    private readonly IConfigurationRoot configuration;
    private readonly NotificationsEnabledDb database;
    private readonly DefaultHttpContext context = new();
    private readonly IGithubOAuthClient githubOAuth = Substitute.For<IGithubOAuthClient>();
    private readonly LoginController controller;
    private readonly OAuthReturnController callback;

    public GithubLoginTests()
    {
        configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "BaseUrl", "http://localhost:5000/" },
            { "Login:Github:ClientId", "github-client" },
            { "Login:Github:ClientSecret", "github-secret" },
        }).Build();
        database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        var csrf = Substitute.For<ITokenVerifier>();
        csrf.IsValidCSRFToken("csrf", null, true).Returns(true);
        controller = new LoginController(NullLogger<LoginController>.Instance, database, configuration, csrf,
            new RedirectVerifier(configuration), Substitute.For<IPatreonAPI>(), Substitute.For<IBackgroundJobClient>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        callback = new OAuthReturnController(NullLogger<OAuthReturnController>.Instance, configuration, database,
            githubOAuth, Substitute.For<ICLAExemptions>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        SetEmails(new GithubEmail { Email = UserEmail, Verified = true, Primary = true });
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task LoginOptionRequiresBothCredentials(bool clientId, bool clientSecret)
    {
        configuration["Login:Github:ClientId"] = clientId ? "client" : null;
        configuration["Login:Github:ClientSecret"] = clientSecret ? "secret" : null;

        var option = Assert.Single(controller.Get().Categories.SelectMany(category => category.Options),
            option => option.InternalName == LoginController.SsoTypeGithub);
        Assert.Equal(clientId && clientSecret, option.Active);
        Assert.False(option.Local);

        var result = Assert.IsType<RedirectResult>(await controller.StartSsoLogin(new SsoStartFormData
        {
            SsoType = LoginController.SsoTypeGithub,
            CSRF = "csrf",
        }));
        if (clientId && clientSecret)
        {
            Assert.StartsWith("https://github.com/login/oauth/authorize?", result.Url);
        }
        else
        {
            Assert.StartsWith("/login?error=", result.Url);
            Assert.Empty(database.Sessions);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("local")]
    [InlineData(LoginController.SsoTypeCommunityForum)]
    [InlineData(LoginController.SsoTypePatreon)]
    [InlineData(LoginController.SsoTypeGithub)]
    public async Task LoginCreatesOrReusesAccountAndRotatesSession(string? source)
    {
        User? existing = null;
        if (source != null)
        {
            existing = new User(UserEmail, "githubuser")
            {
                Local = source == "local",
                SsoSource = source == "local" ? null : source,
            };
            existing.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
            await database.Users.AddAsync(existing);
            await database.SaveChangesAsync();
        }

        var session = await BeginLogin();
        var nonce = session.SsoNonce!;
        var forward = Assert.IsType<RedirectResult>(await callback.GithubReturn(nonce, "code", null));
        Assert.StartsWith("/LoginController/return/github?", forward.Url);
        Assert.Equal(nonce, QueryHelpers.ParseQuery(new Uri("http://localhost" + forward.Url).Query)["state"]);
        Assert.Equal(nonce, session.SsoNonce);

        var result = Assert.IsType<RedirectResult>(await controller.SsoReturnGithub(nonce, "code", null, githubOAuth));

        Assert.Equal("http://localhost:5000/cla/sign", result.Url);
        var loggedInSession = await database.Sessions.Include(s => s.User).SingleAsync();
        Assert.NotEqual(session.Id, loggedInSession.Id);
        Assert.Null(loggedInSession.SsoNonce);
        Assert.Null(loggedInSession.StartedSsoLogin);
        Assert.Null(loggedInSession.SsoReturnUrl);
        Assert.Equal(UserEmail, loggedInSession.User!.Email);
        Assert.Equal(LoginController.SsoTypeGithub, loggedInSession.User.SsoSource);
        Assert.Equal("githubuser", loggedInSession.User.UserName);
        Assert.Single(database.Users);
        if (existing != null)
            Assert.Equal(existing.Id, loggedInSession.UserId);

        await githubOAuth.Received(1).GetUser("code", CallbackUrl);
    }

    [Theory]
    [InlineData("123+user@users.noreply.github.com", true)]
    [InlineData("USER@USERS.NOREPLY.GITHUB.COM", true)]
    [InlineData("noreply@github.com", true)]
    [InlineData("no-reply@example.com", true)]
    [InlineData(UserEmail, false)]
    [InlineData("", true)]
    public async Task LoginRejectsNoreplyAndUnverifiedEmails(string email, bool verified)
    {
        SetEmails(new GithubEmail { Email = email, Verified = verified, Primary = true });
        var session = await BeginLogin();
        var nonce = session.SsoNonce!;

        var result = Assert.IsType<RedirectResult>(await controller.SsoReturnGithub(nonce, "code", null, githubOAuth));

        Assert.Contains("verified email address", WebUtility.UrlDecode(result.Url));
        Assert.Null(session.UserId);
        Assert.Null(session.SsoNonce);
        Assert.Null(session.StartedSsoLogin);
        Assert.Empty(database.Users);

        await controller.SsoReturnGithub(nonce, "code", null, githubOAuth);
        await githubOAuth.Received(1).GetUser(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoginPrefersVerifiedPrimaryEmailAndSkipsNoreply(bool usablePrimary)
    {
        SetEmails(new GithubEmail { Email = "unverified@example.com", Primary = true },
            new GithubEmail { Email = "fallback@example.com", Verified = true },
            new GithubEmail
            {
                Email = usablePrimary ? UserEmail : "123+user@users.noreply.github.com",
                Verified = true,
                Primary = true,
            });
        var session = await BeginLogin();

        await controller.SsoReturnGithub(session.SsoNonce!, "code", null, githubOAuth);

        Assert.Equal(usablePrimary ? UserEmail : "fallback@example.com", (await database.Users.SingleAsync()).Email);
    }

    [Theory]
    [InlineData("nonce")]
    [InlineData("expired")]
    [InlineData("ip")]
    [InlineData("source")]
    public async Task InvalidSessionIsRejectedBeforeContactingGithub(string failure)
    {
        var session = await BeginLogin();
        var nonce = session.SsoNonce!;
        switch (failure)
        {
            case "nonce":
                nonce = "wrong";
                break;
            case "expired":
                session.SsoStartTime = DateTime.UtcNow.AddHours(-1);
                break;
            case "ip":
                context.Connection.RemoteIpAddress = IPAddress.Parse("127.1.2.3");
                break;
            case "source":
                session.StartedSsoLogin = OAuthController.GithubCLASource;
                break;
        }

        var result = Assert.IsType<RedirectResult>(await controller.SsoReturnGithub(nonce, "code", null, githubOAuth));

        Assert.StartsWith("/login?error=", result.Url);
        Assert.Null(session.UserId);
        Assert.Empty(database.Users);
        await githubOAuth.DidNotReceive().GetUser(Arg.Any<string>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("api")]
    public async Task FailedAuthorizationConsumesNonce(string failure)
    {
        var session = await BeginLogin();
        var nonce = session.SsoNonce!;
        githubOAuth.GetUser("code", CallbackUrl).Returns(_ =>
            Task.FromException<(GithubUserInfo, List<GithubEmail>)>(new Exception("GitHub failed")));

        var result = Assert.IsType<RedirectResult>(await controller.SsoReturnGithub(nonce,
            failure == "api" ? "code" : null, failure == "cancelled" ? "access_denied" : null, githubOAuth));

        Assert.StartsWith("/login?error=", result.Url);
        Assert.Null(session.SsoNonce);
        Assert.Null(session.StartedSsoLogin);
        Assert.Empty(database.Users);
    }

    [Theory]
    [InlineData("suspended")]
    [InlineData("developer-source")]
    [InlineData("developer-group")]
    public async Task LoginRespectsSuspensionAndDeveloperAccountRestrictions(string restriction)
    {
        bool developer = restriction != "suspended";
        var user = new User(UserEmail, "githubuser")
        {
            SsoSource = restriction == "developer-source" ?
                LoginController.SsoTypeDevForum :
                LoginController.SsoTypeGithub,
            SuspendedUntil = developer ? null : DateTime.UtcNow.AddDays(1),
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(developer ? GroupType.Developer : GroupType.User));
        await database.Users.AddAsync(user);
        await database.SaveChangesAsync();
        var session = await BeginLogin();

        var result = Assert.IsType<RedirectResult>(await controller.SsoReturnGithub(session.SsoNonce!, "code", null,
            githubOAuth));

        Assert.Contains(developer ? "developer account" : "suspended", WebUtility.UrlDecode(result.Url));
        Assert.Null(session.UserId);
    }

    [Fact]
    public async Task LoginRejectsExternalReturnUrl()
    {
        var session = await BeginLogin("https://other.example.com/");

        var result = Assert.IsType<RedirectResult>(await controller.SsoReturnGithub(session.SsoNonce!, "code", null,
            githubOAuth));

        Assert.Equal("/", result.Url);
        Assert.NotNull((await database.Sessions.SingleAsync()).UserId);
    }

    [Fact]
    public async Task ClaCallbackStillAttachesGithubWithoutLoggingIn()
    {
        var session = await BeginLogin();
        session.StartedSsoLogin = OAuthController.GithubCLASource;
        session.SsoReturnUrl = CallbackUrl;
        var signature = new InProgressClaSignature { SessionId = session.Id };
        await database.InProgressClaSignatures.AddAsync(signature);
        await database.SaveChangesAsync();

        var result = Assert.IsType<RedirectResult>(await callback.GithubReturn(session.SsoNonce!, "code", null));

        Assert.Equal("/cla/sign", result.Url);
        Assert.Equal("githubuser", signature.GithubAccount);
        Assert.Equal(123, signature.GithubUserId);
        Assert.Equal(UserEmail, signature.GithubEmail);
        Assert.Null(session.UserId);
        Assert.Empty(database.Users);
    }

    public void Dispose()
    {
        controller.Dispose();
        callback.Dispose();
        database.Dispose();
    }

    private async Task<Session> BeginLogin(string returnUrl = "http://localhost:5000/cla/sign")
    {
        var result = Assert.IsType<RedirectResult>(await controller.StartSsoLogin(new SsoStartFormData
        {
            SsoType = LoginController.SsoTypeGithub,
            CSRF = "csrf",
            ReturnUrl = returnUrl,
        }));
        var query = QueryHelpers.ParseQuery(new Uri(result.Url).Query);
        Assert.Equal(CallbackUrl, query["redirect_uri"]);
        Assert.Equal(GithubOAuthClient.RequiredScopes, query["scope"]);
        var session = await database.Sessions.SingleAsync();
        Assert.Equal(session.SsoNonce, query["state"]);
        context.Request.Headers.Cookie = $"{AppInfo.SessionCookieName}={session.Id}:-1";
        return session;
    }

    private void SetEmails(params GithubEmail[] emails)
    {
        githubOAuth.GetUser("code", CallbackUrl).Returns((new GithubUserInfo { Login = "githubuser", Id = 123 },
            emails.ToList()));
    }
}
