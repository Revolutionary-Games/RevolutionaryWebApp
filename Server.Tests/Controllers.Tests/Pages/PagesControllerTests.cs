namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Server.Authorization;
using Server.Controllers.Pages;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;
using TestUtilities.Utilities;
using Utilities;
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

    [Fact]
    public async Task PagesController_OldVersionCreationWorks()
    {
        var jobsMock = Substitute.For<IBackgroundJobClient>();
        var senderMock = Substitute.For<IModelUpdateNotificationSender>();
        var notificationsMock = Substitute.For<IHubContext<NotificationsHub, INotifications>>();

        var database =
            new EditableInMemoryDatabaseFixtureWithNotifications(senderMock, "VersionedPageCreatesOldVersion");

        var controller =
            new PagesController(logger, database.NotificationsEnabledDatabase, jobsMock, notificationsMock)
            {
                UsePageUpdateTransaction = false,
            };

        Assert.Null(await fixture.Database.VersionedPages.FindAsync(1L));
        var versions = await database.Database.PageVersions.Where(v => v.PageId == 1).ToListAsync();

        Assert.Empty(versions);

        var user = new User("test@example.com", "test2")
        {
            Id = 1,
            Local = true,
            Groups = new List<UserGroup>
            {
                new(GroupType.PostPublisher, GroupType.PostPublisher.ToString()),
            },
        };

        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(user);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        await database.Database.Users.AddAsync(user);
        await database.Database.SaveChangesAsync();

        var dto = new VersionedPageDTO
        {
            Title = "test title",
            Visibility = PageVisibility.HiddenDraft,
            Permalink = "test",
            Type = PageType.Post,
            LastEditComment = "Initial version",
        };

        var result = await controller.CreatePage(dto);
        Assert.IsType<OkObjectResult>(result);

        var page = await database.Database.VersionedPages.FindAsync(1L);
        Assert.NotNull(page);

        // Page is now created, edit it next

        dto.LatestContent = "This is the page's content";
        dto.LastEditComment = "Update comment";

        result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkObjectResult>(result);

        versions = await database.Database.PageVersions.Where(v => v.PageId == 1).ToListAsync();

        Assert.Single(versions);

        var version = versions.First();

        Assert.Equal(0, version.Version);
        Assert.Equal(page.Id, version.PageId);
        Assert.False(version.Deleted);
        Assert.Equal("Initial version", version.EditComment);
        Assert.Equal(user.Id, version.EditedById);
        Assert.NotEmpty(version.ReverseDiff);

        // Do a second

        dto.LatestContent = "This is the page's content\nWith a bit more content now";
        dto.LastEditComment = "Another comment";

        result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkObjectResult>(result);

        versions = await database.Database.PageVersions.Where(v => v.PageId == 1).OrderBy(v => v.Version).ToListAsync();

        Assert.Equal(2, versions.Count);

        version = versions.First();

        Assert.Equal(0, version.Version);
        Assert.Equal(page.Id, version.PageId);
        Assert.False(version.Deleted);
        Assert.Equal("Initial version", version.EditComment);
        Assert.Equal(user.Id, version.EditedById);
        Assert.NotEmpty(version.ReverseDiff);

        version = versions.Last();

        Assert.Equal(1, version.Version);
        Assert.Equal(page.Id, version.PageId);
        Assert.False(version.Deleted);
        Assert.Equal("Update comment", version.EditComment);
        Assert.Equal(user.Id, version.EditedById);
        Assert.NotEmpty(version.ReverseDiff);
    }

    [Fact]
    public void PagesController_RevertingOldVersionsWork()
    {
        var fixture = new EditableInMemoryDatabaseFixture("VersionedPageRevertWorks");
    }

    public void Dispose()
    {
        logger.Dispose();
    }
}
