namespace ThriveDevCenter.Server.Tests.Filters.Tests;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AngleSharp.Io;
using Fixtures;
using Hangfire;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Server.Authorization;
using Server.Filters;
using Server.Models;
using Server.Services;
using Shared;
using Xunit;

public class RateLimitTests : IClassFixture<SimpleFewUsersDatabaseWithNotifications>
{
    private const string CSRF = "randomCSRF";

    private const int LoginCountEnsuredToHitLimit = 20;
    private const int NonLoggedInUserEnsuredToHitLimit = 500;
    private const int LoggedInNotYetLimit = 700;
    private const int LoggedInUserEnsuredToHitLimit = 1100;

    private readonly ApplicationDbContext database;
    private readonly SimpleFewUsersDatabaseWithNotifications users;

    public RateLimitTests(SimpleFewUsersDatabaseWithNotifications fixture)
    {
        users = fixture;
        database = fixture.Database;
    }

    [Fact]
    public async Task RateLimit_LoginEndpointHasLowLimit()
    {
        var csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(CSRF, null, true))
            .Returns(true).Verifiable();

        using var host = await CreateHost(csrfMock);

        var client = host.GetTestClient();

        var requestContent = new FormUrlEncodedContent(
            new KeyValuePair<string, string>[]
            {
                new("Email", "test@example.com"),
                new("Password", "some_password"),
                new("CSRF", CSRF),
            });

        var response = await client.PostAsync("/LoginController/login", requestContent);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        bool found = false;

        for (int i = 0; i < LoginCountEnsuredToHitLimit; ++i)
        {
            response = await client.PostAsync("/LoginController/login", requestContent);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            Assert.Fail("Expected to hit login rate limit, didn't hit it");
        }

        csrfMock.Verify();
        csrfMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RateLimit_NonLoggedInRunsIntoLimit()
    {
        var csrfMock = new Mock<ITokenVerifier>();
        using var host = await CreateHost(csrfMock);

        var client = host.GetTestClient();

        var response = await client.GetAsync("/dummy");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        bool found = false;

        for (int i = 0; i < NonLoggedInUserEnsuredToHitLimit; ++i)
        {
            response = await client.GetAsync("/dummy");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            Assert.Fail("Expected to hit rate limit, but didn't hit it");
        }

        csrfMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RateLimit_LoggedInUserHasHigherLimit()
    {
        Assert.True(LoggedInNotYetLimit > NonLoggedInUserEnsuredToHitLimit);

        var user1 = await database.Users.FindAsync(1L);

        var csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(CSRF, user1, true))
            .Returns(true).Verifiable();

        using var host = await CreateHost(csrfMock);

        var client = host.GetTestClient();

        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", CSRF);

        var response = await client.GetAsync("/dummy");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        bool found = false;

        for (int i = 0; i < LoggedInUserEnsuredToHitLimit; ++i)
        {
            response = await client.GetAsync("/dummy");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (i <= LoggedInNotYetLimit)
                {
                    Assert.Fail("Logged in user hit rate limit too soon");
                }

                found = true;
                break;
            }
        }

        if (!found)
        {
            Assert.Fail("Expected to hit user rate limit, didn't hit it");
        }

        csrfMock.Verify();
        csrfMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RateLimit_UserCanDoActionsEvenIfAnonymousIsBlocked()
    {
        var user1 = await database.Users.FindAsync(1L);

        var csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(CSRF, user1, true))
            .Returns(true).Verifiable();

        using var host = await CreateHost(csrfMock);

        var client = host.GetTestClient();

        var response = await client.GetAsync("/dummy");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        bool found = false;

        for (int i = 0; i < NonLoggedInUserEnsuredToHitLimit; ++i)
        {
            response = await client.GetAsync("/dummy");

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            Assert.Fail("Expected to hit rate limit, but didn't hit it");
        }

        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", CSRF);

        response = await client.GetAsync("/dummy");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        for (int i = 0; i < LoggedInNotYetLimit; ++i)
        {
            response = await client.GetAsync("/dummy");

            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
    }

    private async Task<IHost> CreateHost(Mock<ITokenVerifier> csrfMock)
    {
        var limitOptions = new MyRateLimitOptions();
        var patreonMock = new Mock<IPatreonAPI>();
        var jobClientMock = new Mock<IBackgroundJobClient>();

        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .UseConfiguration(new ConfigurationBuilder().AddInMemoryCollection(
                        new List<KeyValuePair<string, string?>>
                        {
                            new("BaseUrl", "http://localhost/test/"),
                            new("Login:Local:Enabled", "true"),
                        }).Build())
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(database);
                        services.AddSingleton(users.NotificationsEnabledDatabase);
                        services.AddSingleton(csrfMock.Object);
                        services.AddSingleton(patreonMock.Object);
                        services.AddSingleton(jobClientMock.Object);

                        services.AddSingleton<RedirectVerifier>();
                        services.AddSingleton<CustomMemoryCache>();
                        services.AddScoped<TokenOrCookieAuthenticationMiddleware>();
                        services.AddScoped<CSRFCheckerMiddleware>();

                        services.AddRateLimiter(limiterOptions =>
                        {
                            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                            limiterOptions.OnRejected = CustomRateLimiter.OnRejected;

                            limiterOptions.GlobalLimiter = CustomRateLimiter.CreateGlobalLimiter(limitOptions);

                            CustomRateLimiter.CreateLoginLimiter(limiterOptions, limitOptions);
                            CustomRateLimiter.CreateRegistrationLimiter(limiterOptions, limitOptions);
                            CustomRateLimiter.CreateEmailVerificationLimiter(limiterOptions, limitOptions);
                        });

                        services.AddControllers();
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<TokenOrCookieAuthenticationMiddleware>();
                        app.UseMiddleware<CSRFCheckerMiddleware>();
                        app.UseRouting();
                        app.UseRateLimiter();
                        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
                    });
            })
            .StartAsync();
    }
}
