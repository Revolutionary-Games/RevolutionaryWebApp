namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Common.Utilities;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using NSubstitute.Extensions;
using Server.Authorization;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Server.Utilities;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class LoginControllerTests : IDisposable
{
    private const string CSRFValue = "JustSomeRandomString";
    private const string DummyPatreonLogin = "https://test.patreon.example.com";
    private const string PatronEmail = "patron@example.com";

    private const string PatreonReturnCode = "PatreonReturnCode111";

    private const string PatreonClientId = "beef";
    private const string PatreonClientSecret = "ASuperSecureSecret";

    private const string DevBuildRewardTier = "tierBuilds-1234";

    private readonly XunitLogger<LoginController> logger;

    private readonly IPAddress testIp = IPAddress.Parse("127.1.2.3");

    private readonly PatreonAPIBearerToken testBearerToken = new()
    {
        AccessToken = "TestAccessTokenFromPatreon",
        ExpiresIn = 12345,
        TokenType = "Bearer",
    };

    private readonly PatreonAPIObjectResponse testPatreonUserDetails = new()
    {
        Data = new PatreonObjectData
        {
            Id = "patron-1234",
            Type = "pledge",
            Attributes = new PatreonObjectAttributes
            {
                Email = PatronEmail,
                AmountCents = 500,
            },
        },
    };

    public LoginControllerTests(ITestOutputHelper output)
    {
        logger = new XunitLogger<LoginController>(output);
    }

    [Fact]
    public void LoginController_DiscourseGroupMembershipCheckWorks()
    {
        StringValues groups = new StringValues(new[]
        {
            "Supporter", "Developer", "VIP_supporter", "trust_level_0", "trust_level_2", "trust_level_3",
            "trust_level_1",
        });

        Assert.Contains(groups, group =>
            DiscourseApiHelpers.CommunityDevBuildGroup.Equals(group) ||
            DiscourseApiHelpers.CommunityVIPGroup.Equals(group));

        var blockString =
            "Supporter,Developer,VIP_supporter,trust_level_0,trust_level_2,trust_level_3,trust_level_1";

        var parsedGroups = blockString.Split(',');

        Assert.Contains(parsedGroups, group =>
            DiscourseApiHelpers.CommunityDevBuildGroup.Equals(group) ||
            DiscourseApiHelpers.CommunityVIPGroup.Equals(group));
    }

    [Fact]
    public async Task LoginController_LocalUserLoginWorks()
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(CSRFValue, null, true).Returns(true);
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var jobClientMock = Substitute.For<IBackgroundJobClient>();
        var patreonMock = Substitute.For<IPatreonAPI>();

        string? seenSessionId = null;
        long? seenUserId = null;

        var cookiesMock = Substitute.For<IResponseCookies>();
        cookiesMock.When(cookies =>
                cookies.Append(AppInfo.SessionCookieName, Arg.Any<string>(), Arg.Any<CookieOptions>()))
            .Do(x =>
            {
                if (!x.Arg<CookieOptions>().HttpOnly)
                    Assert.Fail("Login cookie should be HTTP only");

                if (!x.Arg<CookieOptions>().IsEssential)
                    Assert.Fail("Login cookie should be essential");

                if (x.Arg<CookieOptions>().Expires == null ||
                    x.Arg<CookieOptions>().Expires <= DateTime.UtcNow + TimeSpan.FromSeconds(5))
                    Assert.Fail("Already expired login cookie");

                var raw = x.ArgAt<string>(1).Split(':');
                seenSessionId = raw[0];
                seenUserId = long.Parse(raw[1]);
            });

        var connectionMock = Substitute.For<ConnectionInfo>();

        var requestCookiesMock = Substitute.For<IRequestCookieCollection>();

        var httpResponseMock = Substitute.For<HttpResponse>();
        httpResponseMock.Configure().Cookies.Returns(cookiesMock);

        var httpRequestMock = Substitute.For<HttpRequest>();
        httpRequestMock.Configure().Cookies.Returns(requestCookiesMock);

        var httpContextMock = Substitute.For<HttpContext>();
        httpContextMock.Configure().Response.Returns(httpResponseMock);
        httpContextMock.Configure().Request.Returns(httpRequestMock);
        httpContextMock.Configure().Connection.Returns(connectionMock);

        var configuration = CreateConfiguration(true, false);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_LocalUserLoginWorks), notificationsMock);

        var password = "TestUser&Password5";

        var user = new User("test+login@example.com", "test")
        {
            Local = true,
            UserName = "test",
            PasswordHash = Passwords.CreateSaltedPasswordHash(password, [55, 12, 55, 50]),
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        Assert.Empty(database.Sessions);

        var controller = new LoginController(logger, database, configuration, csrfMock,
            new RedirectVerifier(configuration), patreonMock, jobClientMock);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var result = await controller.PerformLocalLogin(new LoginFormData
        {
            Email = user.Email,
            Password = password,
            CSRF = CSRFValue,
        });

        var redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.False(redirectResult.Permanent);
        Assert.Equal("/", redirectResult.Url);

        Assert.NotEmpty(database.Sessions);

        Assert.NotNull(seenSessionId);
        var session = await database.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == Guid.Parse(seenSessionId));

        Assert.NotNull(session);

        Assert.Equal(session.HashedId, SelectByHashedProperty.HashForDatabaseValue(seenSessionId));
        Assert.Equal(user, session.User);
        Assert.Equal(user.Id, seenUserId);

        csrfMock.Received().IsValidCSRFToken(CSRFValue, null, true);
        cookiesMock.Received().Append(AppInfo.SessionCookieName, Arg.Any<string>(), Arg.Any<CookieOptions>());
    }

    [Fact]
    public async Task LoginController_SuspendedCannotLogin()
    {
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(CSRFValue, null, true).Returns(true);
        var notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        var jobClientMock = Substitute.For<IBackgroundJobClient>();
        var patreonMock = Substitute.For<IPatreonAPI>();

        var configuration = CreateConfiguration(true, false);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_SuspendedCannotLogin), notificationsMock);

        var password = "TestUser&Password5";

        var user = new User("test+login@example.com", "test")
        {
            Local = true,
            SuspendedUntil = DateTime.UtcNow + TimeSpan.FromDays(30),
            PasswordHash = Passwords.CreateSaltedPasswordHash(password, [55, 12, 55, 50]),
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        Assert.Empty(database.Sessions);

        var controller = new LoginController(logger, database, configuration, csrfMock,
            new RedirectVerifier(configuration), patreonMock, jobClientMock);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        var result = await controller.PerformLocalLogin(new LoginFormData
        {
            Email = user.Email,
            Password = password,
            CSRF = CSRFValue,
        });

        var redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.False(redirectResult.Permanent);
        Assert.StartsWith("/login", redirectResult.Url);
        Assert.Contains("error=", redirectResult.Url);
        Assert.Contains("suspended", redirectResult.Url);

        Assert.Empty(database.Sessions);
    }

    [Fact]
    public async Task LoginController_AppliesMissingPatronGroup()
    {
        string? seenSessionId = null;
        var cookiesMock = Substitute.For<IResponseCookies>();
        cookiesMock.When(cookies =>
                cookies.Append(AppInfo.SessionCookieName, Arg.Any<string>(), Arg.Any<CookieOptions>()))
            .Do(x => { seenSessionId = x.ArgAt<string>(1).Split(':')[0]; });

        SetupPatronMocks(cookiesMock, out var csrfMock, out var notificationsMock, out var jobClientMock,
            out var patreonMock, out var requestCookiesMock, out var httpContextMock);

        var configuration = CreateConfiguration(false, true);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_AppliesMissingPatronGroup), notificationsMock);

        await SeedPatronData(database);

        var user = new User(PatronEmail, "Mr. Patron")
        {
            Local = false,
            SsoSource = LoginController.SsoTypePatreon,
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        Assert.Empty(database.Sessions);

        var controller = new LoginController(logger, database, configuration, csrfMock,
            new RedirectVerifier(configuration), patreonMock, jobClientMock);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        // Perform start login request
        var result = await controller.StartSsoLogin(new SsoStartFormData
        {
            SsoType = LoginController.SsoTypePatreon,
            CSRF = CSRFValue,
        });

        Assert.DoesNotContain(user.Groups, g => g.Id == GroupType.PatreonSupporter);

        var redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.False(redirectResult.Permanent);
        Assert.StartsWith(DummyPatreonLogin, redirectResult.Url);
        IReadOnlyDictionary<string, string> data = QueryHelpers.ParseQuery(redirectResult.Url).SelectFirstStringValue();

        Assert.Contains("state", data);

        Assert.NotNull(seenSessionId);
        var session = await database.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == Guid.Parse(seenSessionId));

        var firstSessionId = seenSessionId;

        Assert.NotNull(session);
        Assert.Null(session.User);

        Assert.Equal(LoginController.SsoTypePatreon, session.StartedSsoLogin);
        Assert.NotNull(session.SsoNonce);

        // Perform return request
        requestCookiesMock.TryGetValue(AppInfo.SessionCookieName, out Arg.Any<string>()!)
            .Returns(x =>
            {
                x[1] = seenSessionId + ":-1";
                return true;
            });

        result = await controller.SsoReturnPatreon(data["state"], PatreonReturnCode, null);

        redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.False(redirectResult.Permanent);
        Assert.Equal("/", redirectResult.Url);

        // Session gets renamed on successful login
        Assert.NotEqual(firstSessionId, seenSessionId);
        var newSession = await database.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == Guid.Parse(seenSessionId));

        Assert.NotNull(newSession);

        Assert.NotEqual(session, newSession);

        // The first session is deleted
        Assert.Null(await database.Sessions.FirstOrDefaultAsync(s => s.Id == session.Id));

        Assert.Null(newSession.StartedSsoLogin);
        Assert.Null(newSession.SsoNonce);
        Assert.False(user.Suspended);

        var user2 = await database.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(user2);
        Assert.Contains(user2.Groups, g => g.Id == GroupType.PatreonSupporter);

        Assert.Equal(user, newSession.User);

        VerifyPatreonCalls(patreonMock);

        cookiesMock.Received().Append(AppInfo.SessionCookieName, Arg.Any<string>(), Arg.Any<CookieOptions>());
        requestCookiesMock.Received().TryGetValue(Arg.Any<string>(), out Arg.Any<string>()!);
    }

    [Fact]
    public async Task LoginController_PatreonLoginCreatesAccount()
    {
        string? seenSessionId = null;
        var cookiesMock = Substitute.For<IResponseCookies>();
        cookiesMock.When(cookies =>
                cookies.Append(AppInfo.SessionCookieName, Arg.Any<string>(), Arg.Any<CookieOptions>()))
            .Do(x => { seenSessionId = x.ArgAt<string>(1).Split(':')[0]; });

        SetupPatronMocks(cookiesMock, out var csrfMock, out var notificationsMock, out var jobClientMock,
            out var patreonMock, out var requestCookiesMock, out var httpContextMock);

        var configuration = CreateConfiguration(false, true);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_PatreonLoginCreatesAccount), notificationsMock);

        await SeedPatronData(database);

        Assert.Null(await database.Users.FirstOrDefaultAsync(u => u.Email == PatronEmail));

        var controller = new LoginController(logger, database, configuration, csrfMock,
            new RedirectVerifier(configuration), patreonMock, jobClientMock);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        // Perform start login request
        var result = await controller.StartSsoLogin(new SsoStartFormData
        {
            SsoType = LoginController.SsoTypePatreon,
            CSRF = CSRFValue,
        });

        var redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.False(redirectResult.Permanent);
        Assert.StartsWith(DummyPatreonLogin, redirectResult.Url);
        IReadOnlyDictionary<string, string> data = QueryHelpers.ParseQuery(redirectResult.Url).SelectFirstStringValue();
        Assert.Contains("state", data);

        Assert.NotNull(seenSessionId);
        var session = await database.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == Guid.Parse(seenSessionId));

        Assert.NotNull(session);
        Assert.Null(session.User);

        Assert.Equal(LoginController.SsoTypePatreon, session.StartedSsoLogin);
        Assert.NotNull(session.SsoNonce);

        // Perform return request
        requestCookiesMock.TryGetValue(AppInfo.SessionCookieName, out Arg.Any<string>()!)
            .Returns(x =>
            {
                x[1] = seenSessionId + ":-1";
                return true;
            });

        result = await controller.SsoReturnPatreon(data["state"], PatreonReturnCode, null);

        redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.False(redirectResult.Permanent);
        Assert.Equal("/", redirectResult.Url);

        var newSession = await database.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == Guid.Parse(seenSessionId));

        Assert.NotNull(newSession);

        Assert.Null(newSession.StartedSsoLogin);
        Assert.Null(newSession.SsoNonce);

        var user = await database.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Email == PatronEmail);

        Assert.NotNull(user);
        Assert.False(user.Suspended);
        Assert.Contains(user.Groups, g => g.Id == GroupType.PatreonSupporter);

        Assert.Equal(user, newSession.User);

        VerifyPatreonCalls(patreonMock);
    }

    [Fact]
    public async Task LoginController_AutoUnsuspendDoesNotOverrideManualSuspension()
    {
        // Note that log in no longer auto unsuspends, so this test shouldn't be able to detect anything any more...

        string? seenSessionId = null;
        var cookiesMock = Substitute.For<IResponseCookies>();
        cookiesMock.When(cookies =>
                cookies.Append(AppInfo.SessionCookieName, Arg.Any<string>(), Arg.Any<CookieOptions>()))
            .Do(x => { seenSessionId = x.ArgAt<string>(1).Split(':')[0]; });

        SetupPatronMocks(cookiesMock, out var csrfMock, out var notificationsMock, out var jobClientMock,
            out var patreonMock, out var requestCookiesMock, out var httpContextMock);

        var configuration = CreateConfiguration(false, true);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_AutoUnsuspendDoesNotOverrideManualSuspension),
                notificationsMock);

        await SeedPatronData(database);

        var user = new User(PatronEmail, "Mr. Patron")
        {
            Local = false,
            SsoSource = LoginController.SsoTypePatreon,
            SuspendedUntil = DateTime.UtcNow + TimeSpan.FromDays(30),
            SuspendedManually = true,
            SuspendedReason = "aaa",
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        var controller = new LoginController(logger, database, configuration, csrfMock,
            new RedirectVerifier(configuration), patreonMock, jobClientMock);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        // Perform start login request
        var result = await controller.StartSsoLogin(new SsoStartFormData
        {
            SsoType = LoginController.SsoTypePatreon,
            CSRF = CSRFValue,
        });

        var redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);
        IReadOnlyDictionary<string, string> data = QueryHelpers.ParseQuery(redirectResult.Url).SelectFirstStringValue();

        Assert.NotNull(seenSessionId);
        var session = await database.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == Guid.Parse(seenSessionId));

        Assert.NotNull(session);
        Assert.Null(session.User);

        // Perform return request
        requestCookiesMock.TryGetValue(AppInfo.SessionCookieName, out Arg.Any<string>()!)
            .Returns(x =>
            {
                x[1] = seenSessionId + ":-1";
                return true;
            });

        result = await controller.SsoReturnPatreon(data["state"], PatreonReturnCode, null);

        redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.False(redirectResult.Permanent);
        Assert.StartsWith("/login?error", redirectResult.Url);

        Assert.Null(session.SsoNonce);
        Assert.Null(session.User);

        Assert.True(user.Suspended);
    }

    [Theory]
    [InlineData(true, true, DevBuildRewardTier)]
    [InlineData(false, true, DevBuildRewardTier)]
    [InlineData(true, false, "nothing")]
    [InlineData(false, false, "nothing")]
    public async Task LoginController_BadPatronStatusDisallowsLogin(bool userSuspended, bool patronSuspended,
        string rewardTier)
    {
        string? seenSessionId = null;
        var cookiesMock = Substitute.For<IResponseCookies>();
        cookiesMock.When(cookies =>
                cookies.Append(AppInfo.SessionCookieName, Arg.Any<string>(), Arg.Any<CookieOptions>()))
            .Do(x => { seenSessionId = x.ArgAt<string>(1).Split(':')[0]; });

        SetupPatronMocks(cookiesMock, out var csrfMock, out var notificationsMock, out var jobClientMock,
            out var patreonMock, out var requestCookiesMock, out var httpContextMock);

        var configuration = CreateConfiguration(false, true);

        await using var database =
            CreateMemoryDatabase(
                nameof(LoginController_BadPatronStatusDisallowsLogin) +
                $"{userSuspended}-{patronSuspended}-{rewardTier}", notificationsMock);

        await SeedPatronData(database, patronSuspended, rewardTier);

        var user = new User(PatronEmail, "Mr. Patron")
        {
            Local = false,
            SsoSource = LoginController.SsoTypePatreon,
            SuspendedUntil = userSuspended ? DateTime.UtcNow + TimeSpan.FromDays(30) : null,
            SuspendedManually = false,
            SuspendedReason = "Test suspension reason.",
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        var controller = new LoginController(logger, database, configuration, csrfMock,
            new RedirectVerifier(configuration), patreonMock, jobClientMock);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        // Perform start login request
        var result = await controller.StartSsoLogin(new SsoStartFormData
        {
            SsoType = LoginController.SsoTypePatreon,
            CSRF = CSRFValue,
        });

        var redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);
        var data = QueryHelpers.ParseQuery(redirectResult.Url).SelectFirstStringValue();

        Assert.NotNull(seenSessionId);
        var session = await database.Sessions.Include(s => s.User)
            .FirstOrDefaultAsync(s => s.Id == Guid.Parse(seenSessionId));

        Assert.NotNull(session);

        // Perform return request
        requestCookiesMock.TryGetValue(AppInfo.SessionCookieName, out Arg.Any<string>()!)
            .Returns(x =>
            {
                x[1] = seenSessionId;
                return true;
            });

        result = await controller.SsoReturnPatreon(data["state"], PatreonReturnCode, null);

        redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.StartsWith("/login?error", redirectResult.Url);

        Assert.Null(session.User);
        Assert.Equal(userSuspended, user.Suspended);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private static IConfigurationRoot CreateConfiguration(bool allowLocalLogin, bool patreonLogin)
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new KeyValuePair<string, string?>[]
        {
            new("BaseUrl", "http://localhost:5000/"),
            new("Login:Local:Enabled", allowLocalLogin.ToString()),
        });

        if (patreonLogin)
        {
            builder.AddInMemoryCollection(new KeyValuePair<string, string?>[]
            {
                new("Login:Patreon:ClientId", PatreonClientId),
                new("Login:Patreon:ClientSecret", PatreonClientSecret),
                new("Login:Patreon:BaseUrl", DummyPatreonLogin),
            });
        }

        return builder.Build();
    }

    private static NotificationsEnabledDb CreateMemoryDatabase(string dbName,
        IModelUpdateNotificationSender notificationSender)
    {
        var dbOptions =
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(dbName).Options;

        var database = new NotificationsEnabledDb(dbOptions, notificationSender);

        return database;
    }

    private static async Task SeedPatronData(NotificationsEnabledDb database, bool suspended = false,
        string? rewardTier = null)
    {
        rewardTier ??= DevBuildRewardTier;

        await database.Patrons.AddAsync(new Patron
        {
            Email = PatronEmail,
            RewardId = rewardTier,
            Username = "Mr. Patron",
            Suspended = suspended,
        });

        await database.PatreonSettings.AddAsync(new PatreonSettings
        {
            Active = true,
            DevbuildsRewardId = DevBuildRewardTier,
            VipRewardId = "4567",
            CreatorToken = "Creator-0101",
        });

        await database.UserGroups.AddAsync(new UserGroup(GroupType.PatreonSupporter, "Patreon Supporter"));

        await database.SaveChangesAsync();
    }

    private void SetupPatronMocks(IResponseCookies cookiesMock, out ITokenVerifier csrfMock,
        out IModelUpdateNotificationSender notificationsMock, out IBackgroundJobClient jobClientMock,
        out IPatreonAPI patreonMock, out IRequestCookieCollection requestCookiesMock,
        out HttpContext httpContextMock)
    {
        csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(CSRFValue, null, true).Returns(true);
        notificationsMock = Substitute.For<IModelUpdateNotificationSender>();
        jobClientMock = Substitute.For<IBackgroundJobClient>();

        patreonMock = Substitute.For<IPatreonAPI>();
        patreonMock.TurnCodeIntoTokens(PatreonReturnCode, Arg.Any<string>()).Returns(Task.FromResult(testBearerToken));
        patreonMock.GetOwnDetails().Returns(Task.FromResult(testPatreonUserDetails));

        var connectionMock = Substitute.For<ConnectionInfo>();
        connectionMock.Configure().RemoteIpAddress.Returns(testIp);

        requestCookiesMock = Substitute.For<IRequestCookieCollection>();

        var httpResponseMock = Substitute.For<HttpResponse>();
        httpResponseMock.Configure().Cookies.Returns(cookiesMock);

        var httpRequestMock = Substitute.For<HttpRequest>();
        httpRequestMock.Configure().Cookies.Returns(requestCookiesMock);

        httpContextMock = Substitute.For<HttpContext>();
        httpContextMock.Configure().Response.Returns(httpResponseMock);
        httpContextMock.Configure().Request.Returns(httpRequestMock);
        httpContextMock.Configure().Connection.Returns(connectionMock);

        httpRequestMock.Configure().HttpContext.Returns(httpContextMock);
    }

    private void VerifyPatreonCalls(IPatreonAPI patreonMock)
    {
        Received.InOrder(() =>
        {
            patreonMock.Initialize(Arg.Any<string>(), Arg.Any<string>());
            patreonMock.LoginAsUser(Arg.Any<PatreonAPIBearerToken>());
        });

        patreonMock.Received().Initialize(PatreonClientId, PatreonClientSecret);
        patreonMock.Received().LoginAsUser(testBearerToken);

        patreonMock.Received().GetOwnDetails();
    }
}
