namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests.Pages;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fixtures;
using Hangfire;
using Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RevolutionaryWebApp.Server.Controllers.Pages;
using RevolutionaryWebApp.Server.Models;
using RevolutionaryWebApp.Server.Models.Pages;
using RevolutionaryWebApp.Shared.Models.Enums;
using RevolutionaryWebApp.Shared.Models.Pages;
using Server.Services;
using Utilities;
using Xunit;

public class PageCachingTests
{
    private readonly ILogger<PagesController> logger = new NullLogger<PagesController>();

    [Fact]
    public async Task ResolveVersionFullContent_CachesEvery10thVersion()
    {
        var jobsMock = Substitute.For<IBackgroundJobClient>();
        var senderMock = Substitute.For<IModelUpdateNotificationSender>();
        var notificationsMock = Substitute.For<IHubContext<NotificationsHub, INotifications>>();
        var cacheMock = Substitute.For<IDistributedCache>();
        cacheMock.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<byte[]?>(null));

        var (controller, page) = await SetupDataForTest(senderMock, jobsMock, notificationsMock, cacheMock,
            "PageCaching_CachesEvery10th");

        var versionToResolve = 1;
        var actionResult = await controller.GetResourceHistoricalVersion(page.Id, versionToResolve);

        PageVersionDTO? resolvedDTO;
        if (actionResult.Value != null)
        {
            resolvedDTO = actionResult.Value;
        }
        else if (actionResult.Result is ObjectResult objectResult)
        {
            resolvedDTO = objectResult.Value as PageVersionDTO;
            if (resolvedDTO == null)
            {
                if (objectResult.Value is ProblemDetails problem)
                {
                    Assert.Fail($"ObjectResult value is not PageVersionDTO, it is ProblemDetails: {problem.Detail}. " +
                        $"Status code: {objectResult.StatusCode}");
                }
                else
                {
                    Assert.Fail($"ObjectResult value is not PageVersionDTO, it is " +
                        $"{objectResult.Value?.GetType().Name ?? "null"}. Status code: {objectResult.StatusCode}");
                }
            }
        }
        else
        {
            Assert.Fail($"Expected value or ObjectResult, got {actionResult.Result?.GetType().Name ?? "null"}");
            return;
        }

        Assert.NotNull(resolvedDTO);
        Assert.Equal("Version 1", resolvedDTO.PageContentAtVersion);

        // Verify that version 10 was cached.
        await cacheMock.Received().SetAsync(Arg.Is<string>(s => s.Contains($":{page.Id}:10")),
            Arg.Any<byte[]>(),
            Arg.Any<DistributedCacheEntryOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveVersionFullContent_CacheReuseWorksAndResumes()
    {
        var jobsMock = Substitute.For<IBackgroundJobClient>();
        var senderMock = Substitute.For<IModelUpdateNotificationSender>();
        var notificationsMock = Substitute.For<IHubContext<NotificationsHub, INotifications>>();
        var cacheMock = Substitute.For<IDistributedCache>();
        cacheMock.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(info =>
            {
                if (info.Arg<string>().EndsWith(":10"))
                {
                    return Task.FromResult<byte[]?>("Version 10"u8.ToArray());
                }

                return Task.FromResult<byte[]?>(null);
            });

        var (controller, page) = await SetupDataForTest(senderMock, jobsMock, notificationsMock, cacheMock,
            "PageCaching_CacheReuseWorksAndResumes");

        var versionToResolve = 10;
        var resolvedDTO = await GetPageVersion(controller, page, versionToResolve);

        Assert.NotNull(resolvedDTO);
        Assert.Equal("Version 10", resolvedDTO.PageContentAtVersion);

        await cacheMock.Received().GetAsync(Arg.Is<string>(s => s.Contains($":{page.Id}:10")),
            Arg.Any<CancellationToken>());

        versionToResolve = 9;
        resolvedDTO = await GetPageVersion(controller, page, versionToResolve);

        Assert.NotNull(resolvedDTO);
        Assert.Equal("Version 9", resolvedDTO.PageContentAtVersion);

        await cacheMock.Received().GetAsync(Arg.Is<string>(s => s.Contains($":{page.Id}:10")),
            Arg.Any<CancellationToken>());
    }

    private static async Task<PageVersionDTO?> GetPageVersion(PagesController controller, VersionedPage page,
        int versionToResolve)
    {
        PageVersionDTO? resolvedDTO = null;
        var actionResult = await controller.GetResourceHistoricalVersion(page.Id, versionToResolve);

        if (actionResult.Value != null)
        {
            resolvedDTO = actionResult.Value;
        }
        else if (actionResult.Result is ObjectResult objectResult)
        {
            resolvedDTO = objectResult.Value as PageVersionDTO;
        }

        return resolvedDTO;
    }

    private async Task<(PagesController Controller, VersionedPage Page)> SetupDataForTest(
        IModelUpdateNotificationSender senderMock, IBackgroundJobClient jobsMock,
        IHubContext<NotificationsHub, INotifications> notificationsMock, IDistributedCache cacheMock, string testName)
    {
        var databaseFixture =
            new EditableInMemoryDatabaseFixtureWithNotifications(senderMock, testName);
        var database = databaseFixture.NotificationsEnabledDatabase;

        var controller = new PagesController(logger, database, jobsMock, notificationsMock, cacheMock)
        {
            UsePageUpdateTransaction = false,
        };

        var user = new User("test@example.com", "test")
        {
            Id = 1,
            Groups = new List<UserGroup> { new(GroupType.SitePageEditor, "Editor") },
        };
        await database.Users.AddAsync(user);
        var httpContextMock = HttpContextMockHelpers.CreateContextWithUser(user);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContextMock,
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMvc();
        controller.ControllerContext.HttpContext.RequestServices = services.BuildServiceProvider();

        // Create a page
        var page = new VersionedPage("Test Page")
        {
            Type = PageType.NormalPage,
            CreatorId = user.Id,
            LatestContent = "Version 1",
            LastEditorId = user.Id,
        };
        await database.VersionedPages.AddAsync(page);
        await database.SaveChangesAsync();

        // Create 15 more versions (total 16 versions, version numbers 1 to 16)
        // version 1 is in PageVersion if we update it.

        for (int i = 2; i <= 16; ++i)
        {
            var dto = new VersionedPageDTO
            {
                Id = page.Id,
                Title = page.Title,
                LatestContent = $"Version {i}",
                VersionNumber = i - 1,
                LastEditComment = $"Edit {i}",
                Visibility = PageVisibility.HiddenDraft,
            };
            var updateResult = await controller.UpdatePage(page.Id, dto);
            Assert.IsType<OkObjectResult>(updateResult);
        }

        return (controller, page);
    }
}
