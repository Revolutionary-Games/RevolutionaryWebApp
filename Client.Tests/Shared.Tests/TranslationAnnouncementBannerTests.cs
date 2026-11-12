namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Notifications;
using RichardSzalay.MockHttp;
using Services;
using Xunit;

public sealed class TranslationAnnouncementBannerTests : BunitContext, IAsyncLifetime
{
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowsCurrentSyncStateAndUpdatesWhenProjectNotificationsArrive(bool initiallyRunning)
    {
        Services.AddSingleton(new CurrentUserInfo());
        Services.AddSingleton(CSRFMockFactory.Create());
        Services.AddSingleton<NotificationHandler>();
        var project = new TranslationProjectDTO
        {
            Id = 42, Slug = "test", OperationInProgress = initiallyRunning,
        };
        var http = Services.AddMockHttpClient();
        var request = http.When(HttpMethod.Get, "/api/v1/translations/projects/test").RespondJson(project);
        var banner = Render<TranslationAnnouncementBanner>(parameters => parameters.Add(item => item.Slug, "test"));

        // Initial state is checked again after subscribing, closing the fetch/subscribe gap.
        await banner.WaitForAssertionAsync(() =>
        {
            Assert.Equal(2, http.GetMatchCount(request));
            Assert.Equal(initiallyRunning, banner.FindAll(".alert-warning").Count == 1);
        });
        var groups = new HashSet<string>();
        banner.Instance.GetWantedListenedGroups(new CachedUserGroups(), groups);
        Assert.Contains(NotificationGroups.TranslationProjectUpdatedPrefix + project.Id, groups);

        project.OperationInProgress = !initiallyRunning;
        await banner.InvokeAsync(() => banner.Instance.Handle(new TranslationProjectUpdated { ProjectId = 99 },
            CancellationToken.None));
        Assert.Equal(2, http.GetMatchCount(request));

        await banner.InvokeAsync(() => banner.Instance.Handle(new TranslationProjectUpdated { ProjectId = 42 },
            CancellationToken.None));
        await banner.WaitForAssertionAsync(() =>
            Assert.Equal(!initiallyRunning, banner.FindAll(".alert-warning").Count == 1));

        project.OperationInProgress = true;
        await banner.InvokeAsync(() => banner.Instance.Handle(new TranslationProjectUpdated { ProjectId = 42 },
            CancellationToken.None));
        await banner.WaitForAssertionAsync(() =>
        {
            var warning = Assert.Single(banner.FindAll(".alert-warning"));
            Assert.Contains("Saving translations may time out or fail", warning.TextContent);
            Assert.Empty(warning.QuerySelectorAll("button"));
        });

        project.OperationInProgress = false;
        await banner.InvokeAsync(() => banner.Instance.Handle(new TranslationProjectUpdated { ProjectId = 42 },
            CancellationToken.None));
        await banner.WaitForAssertionAsync(() => Assert.Empty(banner.FindAll(".alert-warning")));
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return DisposeAsync().AsTask();
    }
}
