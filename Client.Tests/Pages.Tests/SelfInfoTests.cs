namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System;
using System.Linq;
using System.Net.Http;
using Bunit;
using Client.Pages;
using Client.Shared;
using Client.Shared.SinglePageParts;
using Microsoft.Extensions.DependencyInjection;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using RichardSzalay.MockHttp;
using Services;
using Xunit;

public class SelfInfoTests : BunitContext
{
    [Fact]
    public void AdminProfileDisplaysTheFreshContributionCountAndLastKnownIpAddress()
    {
        var http = Services.AddMockHttpClient();
        var userInfo = new CurrentUserInfo();
        var groups = new CachedUserGroups(GroupType.Admin);
        userInfo.OnReceivedOurInfo(new UserDTO { Id = 1, Groups = groups, ContributionLimit = 100000 });
        Services.AddSingleton(userInfo);

        ComponentFactories.AddStub<UserRecentActivity>();
        ComponentFactories.AddStub<UserTokenControls>();
        ComponentFactories.AddStub<UserNameEditor>();
        ComponentFactories.AddStub<LauncherLinkList>();
        ComponentFactories.AddStub<UserSessions>();
        ComponentFactories.AddStub<APIKeys>();
        ComponentFactories.AddStub<TranslationCreditSettings>();
        ComponentFactories.AddStub<TwoFactorSettings>();

        http.Expect(HttpMethod.Get, "/api/v1/UserManagement/1").RespondJson(new UserDTO
        {
            Id = 1, Groups = groups, CurrentContribution = 1000, ContributionLimit = 100000,
            LastKnownIPAddress = "192.0.2.1",
        });

        var page = Render<SelfInfo>();

        page.WaitForAssertion(() => Assert.Equal("Contributions today: 1000 / 100000",
            page.FindAll("li").Single(item =>
                item.TextContent.StartsWith("Contributions today", StringComparison.Ordinal)).TextContent));
        Assert.Contains(page.FindAll("li"), item => item.TextContent == "Last known IP address: 192.0.2.1");
        http.VerifyNoOutstandingExpectation();
    }
}
