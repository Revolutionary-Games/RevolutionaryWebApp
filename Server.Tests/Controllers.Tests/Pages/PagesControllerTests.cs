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
using Server.Models.Pages;
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

        // Page is now created, edit it next (this doesn't create a version yet as the initial content is empty)

        dto.LatestContent = "This is the page's content";
        dto.LastEditComment = "Comment";
        dto.VersionNumber = 1;

        result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkResult>(result);

        versions = await database.Database.PageVersions.Where(v => v.PageId == page.Id).ToListAsync();

        Assert.Empty(versions);
        Assert.Equal(dto.LastEditComment, page.LastEditComment);
        Assert.Equal(dto.LatestContent, page.LatestContent);

        // Second edit, now should create a version

        dto.LatestContent = "This is the page's content\nWith a bit more content now";
        dto.LastEditComment = "Update comment";
        dto.VersionNumber = 1;

        result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkResult>(result);

        versions = await database.Database.PageVersions.Where(v => v.PageId == page.Id).ToListAsync();

        Assert.Single(versions);

        var version = versions.First();

        Assert.Equal(1, version.Version);
        Assert.Equal(page.Id, version.PageId);
        Assert.False(version.Deleted);
        Assert.Equal("Comment", version.EditComment);
        Assert.Equal(user.Id, version.EditedById);
        Assert.NotEmpty(version.ReverseDiff);

        // Third edit

        dto.LatestContent = "This is the page's content\nWith a bit more content now and even more content";
        dto.LastEditComment = "Another comment";
        dto.VersionNumber = 2;

        result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkResult>(result);

        versions = await database.Database.PageVersions.Where(v => v.PageId == page.Id).OrderBy(v => v.Version)
            .ToListAsync();

        Assert.Equal(2, versions.Count);

        version = versions.First();

        Assert.Equal(1, version.Version);
        Assert.Equal(page.Id, version.PageId);
        Assert.False(version.Deleted);
        Assert.Equal("Comment", version.EditComment);
        Assert.Equal(user.Id, version.EditedById);
        Assert.NotEmpty(version.ReverseDiff);

        version = versions.Last();

        Assert.Equal(2, version.Version);
        Assert.Equal(page.Id, version.PageId);
        Assert.False(version.Deleted);
        Assert.Equal("Update comment", version.EditComment);
        Assert.Equal(user.Id, version.EditedById);
        Assert.NotEmpty(version.ReverseDiff);

        Assert.Equal("Another comment", page.LastEditComment);
    }

    [Fact]
    public async Task PagesController_RevertingOldVersionsWork()
    {
        var content1 = """
                       Page content
                       with some stuff
                       """;

        var content2 = """
                       Page content
                       with some stuff
                       and adding even more
                       """;

        var content3 = """
                       Page content
                       with some stuff
                       and adding even more
                       and arriving at a good version
                       """;

        var content4 = """
                       Page content
                       other stuff
                       """;

        var jobsMock = Substitute.For<IBackgroundJobClient>();
        var senderMock = Substitute.For<IModelUpdateNotificationSender>();
        var notificationsMock = Substitute.For<IHubContext<NotificationsHub, INotifications>>();

        var database =
            new EditableInMemoryDatabaseFixtureWithNotifications(senderMock, "VersionedPageRevertWorks");

        var controller =
            new PagesController(logger, database.NotificationsEnabledDatabase, jobsMock, notificationsMock)
            {
                UsePageUpdateTransaction = false,
            };

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
            Title = "Some Title",
            Visibility = PageVisibility.HiddenDraft,
            Permalink = "test",
            Type = PageType.Post,
            LastEditComment = "Initial version",
        };

        await controller.CreatePage(dto);

        var page = await database.Database.VersionedPages.FindAsync(1L);
        Assert.NotNull(page);

        // Do some edits to fill in the edit history

        dto.LatestContent = content1;
        dto.LastEditComment = "Comment 1";
        dto.VersionNumber = 1;

        var result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkResult>(result);
        Assert.Equal(dto.LatestContent, page.LatestContent);

        dto.LatestContent = content2;
        dto.LastEditComment = "Comment 2";
        dto.VersionNumber = 1;

        Assert.NotEqual(dto.LatestContent, page.LatestContent);
        result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkResult>(result);
        Assert.Equal(dto.LatestContent, page.LatestContent);

        dto.LatestContent = content3;
        dto.LastEditComment = "Comment 3";
        dto.VersionNumber = 2;

        result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkResult>(result);
        Assert.Equal(dto.LatestContent, page.LatestContent);

        dto.LatestContent = content4;
        dto.LastEditComment = "Comment 4";
        dto.VersionNumber = 3;

        result = await controller.UpdatePage(page.Id, dto);
        Assert.IsType<OkResult>(result);
        Assert.Equal(dto.LatestContent, page.LatestContent);

        // Check version data is correctly setup
        var historyResult = await controller.ListResourceVersions(page.Id, nameof(PageVersion.Version),
            SortDirection.Ascending,
            0, 20);

        var history = Assert.IsType<ActionResult<PagedResult<PageVersionInfo>>>(historyResult).Value;

        Assert.NotNull(history);
        Assert.Equal(3, history.RowCount);

        // Check that the history entries are correct
        Assert.Equal(1, history.Results[0].Version);
        Assert.Equal("Comment 1", history.Results[0].EditComment);

        Assert.Equal(2, history.Results[1].Version);
        Assert.Equal("Comment 2", history.Results[1].EditComment);

        Assert.Equal(3, history.Results[2].Version);
        Assert.Equal("Comment 3", history.Results[2].EditComment);

        // Check constant stuff that is same for each item
        foreach (var item in history.Results)
        {
            Assert.False(item.Deleted);
            Assert.Equal(page.Id, item.PageId);
            Assert.Equal(user.Id, item.EditedById);
        }

        // Check that original contents match
        await CheckRetrievedContentMatches(controller, page.Id, 1, content1);
        await CheckRetrievedContentMatches(controller, page.Id, 2, content2);
        await CheckRetrievedContentMatches(controller, page.Id, 3, content3);
        await CheckRetrievedContentMatches(controller, page.Id, 4, content4);

        // Try reverting to a few versions
        Assert.NotEqual(content2, page.LatestContent);
        var revertResult = await controller.RevertResourceVersion(page.Id, 2);
        Assert.IsType<OkResult>(revertResult);

        Assert.Equal(content2, page.LatestContent);
        Assert.Equal("Reveretd to stuff", page.LastEditComment);

        // Second revert
        Assert.NotEqual(content4, page.LatestContent);
        revertResult = await controller.RevertResourceVersion(page.Id, 4);
        Assert.IsType<OkResult>(revertResult);

        Assert.Equal(content4, page.LatestContent);
        Assert.Equal("Reveretd to stuff", page.LastEditComment);

        // Check that reverts caused new history entries
        var oldVersionCount = history.RowCount;

        historyResult = await controller.ListResourceVersions(page.Id, nameof(PageVersion.Version),
            SortDirection.Ascending, 0, 20);

        history = Assert.IsType<ActionResult<PagedResult<PageVersionInfo>>>(historyResult).Value;

        Assert.NotNull(history);
        Assert.Equal(oldVersionCount + 2, history.RowCount);

        Assert.Equal("Reverted to version 4", history.Results.Last().EditComment);
    }

    public void Dispose()
    {
        logger.Dispose();
    }

    private async Task CheckRetrievedContentMatches(PagesController controller, long pageId, int version,
        string expected)
    {
        var versionResult = await controller.GetResourceHistoricalVersion(pageId, version);

        var versionDTO = Assert.IsType<ActionResult<PageVersionDTO>>(versionResult).Value;

        Assert.NotNull(versionDTO);

        Assert.NotEmpty(versionDTO.ReverseDiffRaw);

        Assert.Equal(version, versionDTO.Version);
        Assert.Equal(pageId, versionDTO.PageId);

        Assert.Equal(expected, versionDTO.PageContentAtVersion);
    }
}
