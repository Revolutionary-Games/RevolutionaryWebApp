namespace RevolutionaryWebApp.Server.Tests.Authorization.Tests;

using System;
using System.Net;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Server.Authorization;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models.Enums;
using Xunit;

public class APIKeyAuthenticationTests : IClassFixture<RealUnitTestDatabaseFixture>
{
    private readonly RealUnitTestDatabaseFixture fixture;

    public APIKeyAuthenticationTests(RealUnitTestDatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer")]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    public async Task HttpClientCanAuthenticateWithAPIKey(string? scheme)
    {
        var database = fixture.Database;
        var key = new APIKey
        {
            Id = Guid.NewGuid().ToString("N")[..14],
            Secret = Guid.NewGuid().ToString(),
            UserId = 1,
        };
        database.APIKeys.Add(key);
        await database.SaveChangesAsync();

        try
        {
            var cache = new CustomMemoryCache();
            using var cacheLifetime = cache.Cache;
            var middleware = new TokenOrCookieAuthenticationMiddleware(
                NullLogger<TokenOrCookieAuthenticationMiddleware>.Instance, database, cache);
            using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services => services.AddRouting())
                .Configure(app =>
                {
                    app.UseRouting();
                    app.Use(async (context, next) =>
                    {
                        context.Connection.RemoteIpAddress = IPAddress.Loopback;
                        await middleware.InvokeAsync(context, _ => next());
                    });
                    app.UseEndpoints(endpoints => endpoints.MapGet("/api/test", async context =>
                    {
                        Assert.Same(key, context.Items[AppInfo.AuthenticationKeyMiddleWareKey]);
                        Assert.Same(key.User, context.Items[AppInfo.CurrentUserMiddlewareKey]);
                        await context.Response.WriteAsync("Authenticated");
                    }).WithMetadata(new APIKeyScopeAttribute(APIKeyScope.Users)));
                })).StartAsync();

            using var client = host.GetTestClient();
            client.Timeout = TimeSpan.FromMinutes(1);
            var token = $"key:{key.Id}:{key.Secret}";
            if (scheme == null)
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", token);
            }
            else
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(scheme, token);
            }

            using var response = await client.GetAsync("/api/test");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Authenticated", await response.Content.ReadAsStringAsync());

            await database.Entry(key).ReloadAsync();
            Assert.Equal(1, key.TotalUses);
            Assert.Equal(IPAddress.Loopback.ToString(), key.LastUsedIP);
            Assert.NotNull(key.LastUsedAt);
        }
        finally
        {
            database.APIKeys.Remove(key);
            await database.SaveChangesAsync();
        }
    }
}
