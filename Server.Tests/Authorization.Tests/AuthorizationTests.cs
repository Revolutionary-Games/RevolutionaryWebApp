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
using NSubstitute;
using Server.Authorization;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using TestUtilities.Utilities;
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
        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(ArgExtension.IsNotNull<string>(), null, false).Returns(false);

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(database);
                        services.AddSingleton(csrfMock);
                        services.AddSingleton<CustomMemoryCache>();
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

        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(csrfValue, user1, true).Returns(true);

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(database);
                services.AddSingleton(csrfMock);
                services.AddSingleton<CustomMemoryCache>();
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

        csrfMock.Received().IsValidCSRFToken(csrfValue, user1, true);
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

        var csrfMock = Substitute.For<ITokenVerifier>();
        csrfMock.IsValidCSRFToken(csrfValue, user1, true).Returns(true);
        csrfMock.IsValidCSRFToken(csrfValue, user2, true).Returns(true);
        csrfMock.IsValidCSRFToken(csrfValue, user3, true).Returns(true);
        csrfMock.IsValidCSRFToken(csrfValue, user4, true).Returns(true);

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(database);
                services.AddSingleton(csrfMock);
                services.AddSingleton<CustomMemoryCache>();
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

        var resultUser = await response.Content.ReadFromJsonAsync<UserDTO>();

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

        resultUser = await response.Content.ReadFromJsonAsync<UserDTO>();

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

        resultUser = await response.Content.ReadFromJsonAsync<UserDTO>();

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
        csrfMock.Received().IsValidCSRFToken(csrfValue, user1, true);
        csrfMock.Received().IsValidCSRFToken(csrfValue, user2, true);
        csrfMock.Received().IsValidCSRFToken(csrfValue, user3, true);
        csrfMock.Received().IsValidCSRFToken(csrfValue, user4, true);
    }

    [Fact]
    public async Task Authorization_ClassAttributeWorksCorrectly()
    {
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(database);
                services.AddSingleton<CustomMemoryCache>();
                services.AddScoped<TokenOrCookieAuthenticationMiddleware>();

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
                app.UseRouting();
                app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
            }));

        // Test that the attributes stack in an expected way
        await CheckGetResponseCode(server, "/accessFilterTest/nonWorkingNoLogin", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest/nonWorkingNoLogin", users.SessionId1,
            HttpStatusCode.NoContent);

        // "restrictedUser" path access
        await CheckGetResponseCode(server, "/accessFilterTest/restrictedUser", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest/restrictedUser", users.SessionId1,
            HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/restrictedUser", users.SessionId2,
            HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/restrictedUser", users.SessionId3,
            HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/restrictedUser", users.SessionId4,
            HttpStatusCode.NoContent);

        // "user" path access
        await CheckGetResponseCode(server, "/accessFilterTest/user", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest/user", users.SessionId1, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/user", users.SessionId2, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/user", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/user", users.SessionId4, HttpStatusCode.Forbidden);

        // "developer" path access
        await CheckGetResponseCode(server, "/accessFilterTest/developer", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest/developer", users.SessionId1, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest/developer", users.SessionId2, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/developer", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/developer", users.SessionId4, HttpStatusCode.Forbidden);

        // "admin" path access
        await CheckGetResponseCode(server, "/accessFilterTest/admin", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest/admin", users.SessionId1, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest/admin", users.SessionId2, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest/admin", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest/admin", users.SessionId4, HttpStatusCode.Forbidden);

        // "system" path access
        await CheckGetResponseCode(server, "/accessFilterTest/system", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest/system", users.SessionId1, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest/system", users.SessionId2, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest/system", users.SessionId3, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest/system", users.SessionId4, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorization_NoClassAttributeWorksCorrectly()
    {
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(database);
                services.AddSingleton<CustomMemoryCache>();
                services.AddScoped<TokenOrCookieAuthenticationMiddleware>();

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
                app.UseRouting();
                app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
            }));

        // "noLogin" path access
        await CheckGetResponseCode(server, "/accessFilterTest2/noLogin", null, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/noLogin", users.SessionId1, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/noLogin", users.SessionId2, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/noLogin", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/noLogin", users.SessionId4, HttpStatusCode.NoContent);

        // "user" path access
        await CheckGetResponseCode(server, "/accessFilterTest2/user", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest2/user", users.SessionId1, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/user", users.SessionId2, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/user", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/user", users.SessionId4, HttpStatusCode.Forbidden);

        // "developer" path access
        await CheckGetResponseCode(server, "/accessFilterTest2/developer", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest2/developer", users.SessionId1, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest2/developer", users.SessionId2, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/developer", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/developer", users.SessionId4, HttpStatusCode.Forbidden);

        // "admin" path access
        await CheckGetResponseCode(server, "/accessFilterTest2/admin", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/accessFilterTest2/admin", users.SessionId1, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest2/admin", users.SessionId2, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/accessFilterTest2/admin", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/accessFilterTest2/admin", users.SessionId4, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Authorization_GroupAttribute()
    {
        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(database);
                services.AddSingleton<CustomMemoryCache>();
                services.AddScoped<TokenOrCookieAuthenticationMiddleware>();

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
                app.UseRouting();
                app.UseEndpoints(endpoints => { endpoints.MapControllers(); });
            }));

        // Test that the attributes stack in an expected way
        await CheckGetResponseCode(server, "/groupFilterTest/nonWorkingUser", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/groupFilterTest/nonWorkingUser", users.SessionId1,
            HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/groupFilterTest/nonWorkingUser", users.SessionId2,
            HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/groupFilterTest/nonWorkingUser", users.SessionId3,
            HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/groupFilterTest/nonWorkingUser", users.SessionId4,
            HttpStatusCode.Forbidden);

        // "developer" path access
        await CheckGetResponseCode(server, "/groupFilterTest/developer", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/groupFilterTest/developer", users.SessionId1, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/groupFilterTest/developer", users.SessionId2, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/groupFilterTest/developer", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/groupFilterTest/developer", users.SessionId4, HttpStatusCode.Forbidden);

        // "admin" path access
        await CheckGetResponseCode(server, "/groupFilterTest/admin", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/groupFilterTest/admin", users.SessionId1, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/groupFilterTest/admin", users.SessionId2, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/groupFilterTest/admin", users.SessionId3, HttpStatusCode.NoContent);
        await CheckGetResponseCode(server, "/groupFilterTest/admin", users.SessionId4, HttpStatusCode.Forbidden);

        // "system" path access
        await CheckGetResponseCode(server, "/groupFilterTest/system", null, HttpStatusCode.Unauthorized);
        await CheckGetResponseCode(server, "/groupFilterTest/system", users.SessionId1, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/groupFilterTest/system", users.SessionId2, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/groupFilterTest/system", users.SessionId3, HttpStatusCode.Forbidden);
        await CheckGetResponseCode(server, "/groupFilterTest/system", users.SessionId4, HttpStatusCode.Forbidden);
    }

    private async Task CheckGetResponseCode(TestServer server, string uri, Guid? sessionId,
        HttpStatusCode requiredStatusCode)
    {
        var requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, uri).ToString());

        if (sessionId != null)
            requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={sessionId}");

        var response = await requestBuilder.GetAsync();

        Assert.Equal(requiredStatusCode, response.StatusCode);
    }
}
