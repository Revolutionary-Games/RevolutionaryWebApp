namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests.Pages;

using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using AngleSharp.Io;
using BlazorPagination;
using Fixtures;
using Hangfire;
using Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Server.Authorization;
using Server.Controllers.Pages;
using Server.Services;
using Shared;
using Shared.Models.Pages;
using TestUtilities.Utilities;
using Xunit;
using Xunit.Abstractions;

public sealed class PagesControllerTests : IClassFixture<SimpleFewUsersDatabaseWithNotifications>, IDisposable
{
    private readonly XunitLogger<PagesController> logger;
    private readonly SimpleFewUsersDatabaseWithNotifications fixture;

    public PagesControllerTests(ITestOutputHelper output, SimpleFewUsersDatabaseWithNotifications fixture)
    {
        logger = new XunitLogger<PagesController>(output);
        this.fixture = fixture;
    }

    [Fact]
    public async Task PagesController_UserPermissionsAttributesWork()
    {
        var jobsMock = Substitute.For<IBackgroundJobClient>();
        var notificationsMock = Substitute.For<IHubContext<NotificationsHub, INotifications>>();

        using var server = new TestServer(new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(fixture.NotificationsEnabledDatabase);
                services.AddSingleton(fixture.Database);
                services.AddSingleton(notificationsMock);
                services.AddSingleton(jobsMock);
                services.AddSingleton<ILogger<PagesController>>(logger);
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

        var url = new Uri(server.BaseAddress,
            "/api/v1/Pages?sortColumn=Title&sortDirection=Descending&page=1&pageSize=25").ToString();

        // Anon access is blocked
        var response = await server.CreateClient().GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // User who doesn't have access
        var requestBuilder = server.CreateRequest(url);
        requestBuilder.AddHeader(HeaderNames.Cookie,
            $"{AppInfo.SessionCookieName}={fixture.SessionId2}:{SimpleFewUsersDatabase.SessionUserId2}");

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // User who does
        requestBuilder = server.CreateRequest(url);
        requestBuilder.AddHeader(HeaderNames.Cookie,
            $"{AppInfo.SessionCookieName}={fixture.SessionId3}:{SimpleFewUsersDatabase.SessionUserId3}");

        response = await requestBuilder.GetAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var deserialized = await JsonSerializer.DeserializeAsync<PagedResult<VersionedPageInfo>>(
            await response.Content.ReadAsStreamAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(deserialized);
        Assert.NotNull(deserialized.Results);
        Assert.Equal(1, deserialized.CurrentPage);
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
