namespace ThriveDevCenter.Server.Tests.Authorization.Tests;

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using Moq;
using Server.Authorization;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using Xunit;

public class AuthorizationTests : IClassFixture<SimpleFewUsersDatabase>
{
    private readonly ApplicationDbContext database;
    private readonly SimpleFewUsersDatabase users;

    public AuthorizationTests(SimpleFewUsersDatabase fixture)
    {
        users = fixture;
        database = fixture.Database;
    }

    [Fact]
    public async Task Authorization_NonLoggedInCannotAccess()
    {
        var csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(It.IsNotNull<string>(), null, false))
            .Returns(false);

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(database);
                        services.AddSingleton(csrfMock.Object);
                        services.AddScoped<TokenOrCookieAuthenticationMiddleware>();
                        services.AddScoped<CSRFCheckerMiddleware>();

                        services.AddControllers();
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        app.UseMiddleware<TokenOrCookieAuthenticationMiddleware>();
                        app.UseMiddleware<CSRFCheckerMiddleware>();
                        app.UseRouting();
                        app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
                    });
            })
            .StartAsync();

        var response = await host.GetTestClient().GetAsync("/dummy/restrictedUser");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        response = await host.GetTestClient().GetAsync("/dummy/user");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        response = await host.GetTestClient().GetAsync("/dummy/developer");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        response = await host.GetTestClient().GetAsync("/dummy/admin");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorization_UserCanAccessUnauthenticatedPage()
    {
        var csrfValue = "dummyCSRFString";

        var user1 = await database.Users.FindAsync(1L);

        var csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, user1, true))
            .Returns(true).Verifiable();

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(database);
                services.AddSingleton(csrfMock.Object);
                services.AddScoped<TokenOrCookieAuthenticationMiddleware>();
                services.AddScoped<CSRFCheckerMiddleware>();

                services.AddControllers();
                services.AddRouting();
            })
            .Configure(app =>
            {
                app.UseMiddleware<TokenOrCookieAuthenticationMiddleware>();
                app.UseMiddleware<CSRFCheckerMiddleware>();
                app.UseRouting();
                app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
            }));

        var requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        var response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        csrfMock.Verify();
    }

    [Fact]
    public async Task Authorization_UserCanAccessRightPages()
    {
        var csrfValue = "dummyCSRFString";

        var user1 = await database.Users.FindAsync(1L);
        Assert.NotNull(user1);
        var user2 = await database.Users.FindAsync(2L);
        Assert.NotNull(user2);
        var user3 = await database.Users.FindAsync(3L);
        Assert.NotNull(user3);
        var user4 = await database.Users.FindAsync(4L);
        Assert.NotNull(user4);

        var csrfMock = new Mock<ITokenVerifier>();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, user1, true))
            .Returns(true).Verifiable();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, user2, true))
            .Returns(true).Verifiable();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, user3, true))
            .Returns(true).Verifiable();
        csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, user4, true))
            .Returns(true).Verifiable();

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(database);
                services.AddSingleton(csrfMock.Object);
                services.AddScoped<TokenOrCookieAuthenticationMiddleware>();
                services.AddScoped<CSRFCheckerMiddleware>();

                services.AddControllers();
                services.AddRouting();
                services.AddAuthentication(options =>
                {
                    options.DefaultForbidScheme = "forbidScheme";
                    options.AddScheme<MyForbidHandler>("forbidScheme", "Handle Forbidden");
                });
            })
            .Configure(app =>
            {
                app.UseMiddleware<TokenOrCookieAuthenticationMiddleware>();
                app.UseMiddleware<CSRFCheckerMiddleware>();
                app.UseRouting();
                app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
            }));

        // User 1 requests
        var requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/user").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        var response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var resultUser = await response.Content.ReadFromJsonAsync<UserInfo>();

        Assert.NotNull(resultUser);
        Assert.Equal(user1.Id, resultUser.Id);
        Assert.Equal(user1.Email, resultUser.Email);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/restrictedUser").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/developer").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/admin").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // User 2 requests
        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/user").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId2}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        resultUser = await response.Content.ReadFromJsonAsync<UserInfo>();

        Assert.NotNull(resultUser);
        Assert.Equal(user2.Id, resultUser.Id);
        Assert.Equal(user2.Email, resultUser.Email);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/developer").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId2}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/admin").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId2}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // User 3 requests
        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/user").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId3}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        resultUser = await response.Content.ReadFromJsonAsync<UserInfo>();

        Assert.NotNull(resultUser);
        Assert.Equal(user3.Id, resultUser.Id);
        Assert.Equal(user3.Email, resultUser.Email);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/developer").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId3}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/admin").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId3}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // User 4 requests
        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/user").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId4}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/restrictedUser").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId4}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/developer").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId4}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy/admin").ToString());
        requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId4}");
        requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Check that all the users got passed correctly csrf checking
        csrfMock.Verify();
        csrfMock.VerifyNoOtherCalls();
    }
}
