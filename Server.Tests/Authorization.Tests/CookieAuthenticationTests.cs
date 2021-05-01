namespace ThriveDevCenter.Server.Tests.Authorization.Tests
{
    using System;
    using System.Net;
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
    using Xunit;

    public class CookieAuthenticationTests : IClassFixture<SimpleFewUsersDatabase>
    {
        private readonly ApplicationDbContext database;
        private readonly SimpleFewUsersDatabase users;

        public CookieAuthenticationTests(SimpleFewUsersDatabase fixture)
        {
            users = fixture;
            database = fixture.Database;
        }

        [Fact]
        public async Task CookieAuthentication_CSRFNotNeededWithoutCookies()
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
                            // ReSharper disable once AccessToDisposedClosure
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

            var response = await host.GetTestClient().GetAsync("/dummy");

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        [Fact]
        public async Task CookieAuthentication_CSRFIsNeeded()
        {
            var csrfMock = new Mock<ITokenVerifier>();
            csrfMock.Setup(csrf => csrf.IsValidCSRFToken(It.IsNotNull<string>(), null, false))
                .Returns(false);

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
                })
            );

            var requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy").ToString());
            requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");

            var response = await requestBuilder.GetAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();

            Assert.Contains("CSRF", content);
        }

        [Fact]
        public async Task CookieAuthentication_CSRFIsAccepted()
        {
            var csrfValue = "dummyCSRFString";

            var user = await database.Users.FindAsync(1L);

            var csrfMock = new Mock<ITokenVerifier>();
            csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, user, true))
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
                })
            );

            var requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy").ToString());
            requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
            requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

            var response = await requestBuilder.GetAsync();

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            csrfMock.Verify();
        }

        [Fact]
        public async Task CookieAuthentication_AuthorizationWorks()
        {
            var csrfValue = "dummyCSRFString";

            var user = await database.Users.FindAsync(1L);

            var csrfMock = new Mock<ITokenVerifier>();
            csrfMock.Setup(csrf => csrf.IsValidCSRFToken(csrfValue, user, true))
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
                })
            );

            var requestBuilder = server.CreateRequest(new Uri(server.BaseAddress, "/dummy").ToString());
            requestBuilder.AddHeader(HeaderNames.Cookie, $"{AppInfo.SessionCookieName}={users.SessionId1}");
            requestBuilder.AddHeader("X-CSRF-Token", csrfValue);

            var response = await requestBuilder.GetAsync();

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            csrfMock.Verify();
        }
    }
}
