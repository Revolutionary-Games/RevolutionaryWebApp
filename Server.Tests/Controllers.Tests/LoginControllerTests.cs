namespace ThriveDevCenter.Server.Tests.Controllers.Tests;

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
using Moq;
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

    private delegate void CookieDelegate(string key, out string? value);

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
        var csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(CSRFValue, null, true))
            .Returns(true).Verifiable();
        var notificationsMock = new Mock<IModelUpdateNotificationSender>();
        var jobClientMock = new Mock<IBackgroundJobClient>();
        var patreonMock = new Mock<IPatreonAPI>();

        string? seenSessionId = null;

        var cookiesMock = new Mock<IResponseCookies>();
        cookiesMock.Setup(cookies =>
                cookies.Append(AppInfo.SessionCookieName, It.IsAny<string>(), It.IsAny<CookieOptions>()))
            .Callback<string, string, CookieOptions>(
                (_, value, options) =>
                {
                    if (!options.HttpOnly)
                        Assert.Fail("Login cookie should be HTTP only");

                    if (!options.IsEssential)
                        Assert.Fail("Login cookie should be essential");

                    if (options.Expires == null || options.Expires <= DateTime.UtcNow + TimeSpan.FromSeconds(5))
                        Assert.Fail("Already expired login cookie");

                    seenSessionId = value;
                }).Verifiable();

        var connectionMock = new Mock<ConnectionInfo>();

        var requestCookiesMock = new Mock<IRequestCookieCollection>();

        var httpResponseMock = new Mock<HttpResponse>();
        httpResponseMock.SetupGet(response => response.Cookies).Returns(cookiesMock.Object);

        var httpRequestMock = new Mock<HttpRequest>();
        httpRequestMock.SetupGet(request => request.Cookies).Returns(requestCookiesMock.Object);

        var httpContextMock = new Mock<HttpContext>();
        httpContextMock.SetupGet(http => http.Response).Returns(httpResponseMock.Object);
        httpContextMock.SetupGet(http => http.Request).Returns(httpRequestMock.Object);
        httpContextMock.SetupGet(http => http.Connection).Returns(connectionMock.Object);

        var configuration = CreateConfiguration(true, false);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_LocalUserLoginWorks), notificationsMock.Object);

        var password = "TestUser&Password5";

        var user = new User
        {
            Local = true,
            Email = "test+login@example.com",
            UserName = "test",
            Suspended = false,
            PasswordHash = Passwords.CreateSaltedPasswordHash(password, new byte[] { 55, 12, 55, 50 }),
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        Assert.Empty(database.Sessions);

        var controller = new LoginController(logger, database, configuration, csrfMock.Object,
            new RedirectVerifier(configuration), patreonMock.Object, jobClientMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
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

        csrfMock.Verify();
        cookiesMock.Verify();
        cookiesMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task LoginController_SuspendedCannotLogin()
    {
        var csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(CSRFValue, null, true))
            .Returns(true).Verifiable();
        var notificationsMock = new Mock<IModelUpdateNotificationSender>();
        var jobClientMock = new Mock<IBackgroundJobClient>();
        var patreonMock = new Mock<IPatreonAPI>();

        var configuration = CreateConfiguration(true, false);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_SuspendedCannotLogin), notificationsMock.Object);

        var password = "TestUser&Password5";

        var user = new User
        {
            Local = true,
            Email = "test+login@example.com",
            UserName = "test",
            Suspended = true,
            PasswordHash = Passwords.CreateSaltedPasswordHash(password, new byte[] { 55, 12, 55, 50 }),
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        Assert.Empty(database.Sessions);

        var controller = new LoginController(logger, database, configuration, csrfMock.Object,
            new RedirectVerifier(configuration), patreonMock.Object, jobClientMock.Object);

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
    public async Task LoginController_UnsuspendSSOPatron()
    {
        string? seenSessionId = null;
        var cookiesMock = new Mock<IResponseCookies>();
        cookiesMock.Setup(cookies =>
                cookies.Append(AppInfo.SessionCookieName, It.IsAny<string>(), It.IsAny<CookieOptions>()))
            .Callback<string, string, CookieOptions>(
                (_, value, _) => { seenSessionId = value; }).Verifiable();

        SetupPatronMocks(cookiesMock, out var csrfMock, out var notificationsMock, out var jobClientMock,
            out var patreonMock, out var requestCookiesMock, out var httpContextMock);

        var configuration = CreateConfiguration(false, true);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_UnsuspendSSOPatron), notificationsMock.Object);

        await SeedPatronData(database);

        var user = new User
        {
            Local = false,
            SsoSource = LoginController.SsoTypePatreon,
            Email = PatronEmail,
            UserName = "Mr. Patron",
            Suspended = true,
            SuspendedManually = false,
            SuspendedReason = SSOSuspendHandler.LoginOptionNoLongerValidText,
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        Assert.Empty(database.Sessions);

        var controller = new LoginController(logger, database, configuration, csrfMock.Object,
            new RedirectVerifier(configuration), patreonMock.Object, jobClientMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
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
        Assert.True(user.Suspended);

        Assert.Equal(LoginController.SsoTypePatreon, session.StartedSsoLogin);
        Assert.NotNull(session.SsoNonce);

        // Perform return request
        string? dummyCookie;
        requestCookiesMock.Setup(cookies => cookies.TryGetValue(AppInfo.SessionCookieName, out dummyCookie))
            .Callback(new CookieDelegate((string _, out string? value) => { value = seenSessionId; })).Returns(true)
            .Verifiable();

        result = await controller.SsoReturnPatreon(data["state"], PatreonReturnCode, null);

        redirectResult = Assert.IsAssignableFrom<RedirectResult>(result);

        Assert.False(redirectResult.Permanent);
        Assert.Equal("/", redirectResult.Url);

        Assert.Null(session.StartedSsoLogin);
        Assert.Null(session.SsoNonce);
        Assert.False(user.Suspended);

        Assert.Equal(user, session.User);

        patreonMock.Verify();
        patreonMock.VerifyNoOtherCalls();

        requestCookiesMock.Verify();
    }

    [Fact]
    public async Task LoginController_AutoUnsuspendDoesNotOverrideManualSuspension()
    {
        string? seenSessionId = null;
        var cookiesMock = new Mock<IResponseCookies>();
        cookiesMock.Setup(cookies =>
                cookies.Append(AppInfo.SessionCookieName, It.IsAny<string>(), It.IsAny<CookieOptions>()))
            .Callback<string, string, CookieOptions>(
                (_, value, _) => { seenSessionId = value; }).Verifiable();

        SetupPatronMocks(cookiesMock, out var csrfMock, out var notificationsMock, out var jobClientMock,
            out var patreonMock, out var requestCookiesMock, out var httpContextMock);

        var configuration = CreateConfiguration(false, true);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_AutoUnsuspendDoesNotOverrideManualSuspension),
                notificationsMock.Object);

        await SeedPatronData(database);

        var user = new User
        {
            Local = false,
            SsoSource = LoginController.SsoTypePatreon,
            Email = PatronEmail,
            UserName = "Mr. Patron",
            Suspended = true,
            SuspendedManually = true,
            SuspendedReason = SSOSuspendHandler.LoginOptionNoLongerValidText,
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        var controller = new LoginController(logger, database, configuration, csrfMock.Object,
            new RedirectVerifier(configuration), patreonMock.Object, jobClientMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
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
        string? dummyCookie;
        requestCookiesMock.Setup(cookies => cookies.TryGetValue(AppInfo.SessionCookieName, out dummyCookie))
            .Callback(new CookieDelegate((string _, out string? value) => { value = seenSessionId; })).Returns(true)
            .Verifiable();

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
        var cookiesMock = new Mock<IResponseCookies>();
        cookiesMock.Setup(cookies =>
                cookies.Append(AppInfo.SessionCookieName, It.IsAny<string>(), It.IsAny<CookieOptions>()))
            .Callback<string, string, CookieOptions>(
                (_, value, _) => { seenSessionId = value; }).Verifiable();

        SetupPatronMocks(cookiesMock, out var csrfMock, out var notificationsMock, out var jobClientMock,
            out var patreonMock, out var requestCookiesMock, out var httpContextMock);

        var configuration = CreateConfiguration(false, true);

        await using var database =
            CreateMemoryDatabase(nameof(LoginController_AutoUnsuspendDoesNotOverrideManualSuspension),
                notificationsMock.Object);

        await SeedPatronData(database, patronSuspended, rewardTier);

        var user = new User
        {
            Local = false,
            SsoSource = LoginController.SsoTypePatreon,
            Email = PatronEmail,
            UserName = "Mr. Patron",
            Suspended = userSuspended,
            SuspendedManually = false,
            SuspendedReason = SSOSuspendHandler.LoginOptionNoLongerValidText,
        };
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(user);

        await database.SaveChangesAsync();

        var controller = new LoginController(logger, database, configuration, csrfMock.Object,
            new RedirectVerifier(configuration), patreonMock.Object, jobClientMock.Object);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock.Object,
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
        string? dummyCookie;
        requestCookiesMock.Setup(cookies => cookies.TryGetValue(AppInfo.SessionCookieName, out dummyCookie))
            .Callback(new CookieDelegate((string _, out string? value) => { value = seenSessionId; })).Returns(true)
            .Verifiable();

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

        await database.SaveChangesAsync();
    }

    private void SetupPatronMocks(Mock<IResponseCookies> cookiesMock, out Mock<ITokenVerifier> csrfMock,
        out Mock<IModelUpdateNotificationSender> notificationsMock, out Mock<IBackgroundJobClient> jobClientMock,
        out Mock<IPatreonAPI> patreonMock, out Mock<IRequestCookieCollection> requestCookiesMock,
        out Mock<HttpContext> httpContextMock)
    {
        csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(CSRFValue, null, true))
            .Returns(true).Verifiable();
        notificationsMock = new Mock<IModelUpdateNotificationSender>();
        jobClientMock = new Mock<IBackgroundJobClient>();
        patreonMock = new Mock<IPatreonAPI>();
        patreonMock.Setup(patreon => patreon.Initialize(PatreonClientId, PatreonClientSecret)).Verifiable();
        patreonMock.Setup(patreon => patreon.TurnCodeIntoTokens(PatreonReturnCode, It.IsAny<string>()))
            .Returns(Task.FromResult(testBearerToken)).Verifiable();
        patreonMock.Setup(patreon => patreon.LoginAsUser(testBearerToken)).Verifiable();
        patreonMock.Setup(patreon => patreon.GetOwnDetails()).Returns(Task.FromResult(testPatreonUserDetails))
            .Verifiable();

        var connectionMock = new Mock<ConnectionInfo>();
        connectionMock.SetupGet(connection => connection.RemoteIpAddress).Returns(testIp);

        requestCookiesMock = new Mock<IRequestCookieCollection>();

        var httpResponseMock = new Mock<HttpResponse>();
        httpResponseMock.SetupGet(response => response.Cookies).Returns(cookiesMock.Object);

        var httpRequestMock = new Mock<HttpRequest>();
        httpRequestMock.SetupGet(request => request.Cookies).Returns(requestCookiesMock.Object);

        httpContextMock = new Mock<HttpContext>();
        httpContextMock.SetupGet(http => http.Response).Returns(httpResponseMock.Object);
        httpContextMock.SetupGet(http => http.Request).Returns(httpRequestMock.Object);
        httpContextMock.SetupGet(http => http.Connection).Returns(connectionMock.Object);

        httpRequestMock.SetupGet(request => request.HttpContext).Returns(httpContextMock.Object);
    }
}
