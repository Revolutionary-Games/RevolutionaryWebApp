namespace ThriveDevCenter.Server.Tests.Filters.Tests;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
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
using Xunit;

public class RateLimitTests : IClassFixture<SimpleFewUsersDatabaseWithNotifications>
{
    private const string CSRF = "randomCSRF";

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

        for (int i = 0; i < 20; ++i)
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
