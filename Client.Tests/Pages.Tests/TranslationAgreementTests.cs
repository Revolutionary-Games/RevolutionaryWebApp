namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System.Net.Http;
using Bunit;
using Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using RichardSzalay.MockHttp;
using Services;
using Shared;
using Xunit;

public class TranslationAgreementTests : BunitContext
{
    [Fact]
    public void AgreementRequiresCheckboxAndUpdatesUserAndWarning()
    {
        var http = Services.AddMockHttpClient();
        var userInfo = new CurrentUserInfo();
        var user = new UserDTO { Id = 1, Groups = new CachedUserGroups(GroupType.User) };
        userInfo.OnReceivedOurInfo(user);
        Services.AddSingleton(userInfo);
        http.When(HttpMethod.Post, "/api/v1/translationAgreement").RespondJson(new UserDTO
        {
            Id = 1, Groups = user.Groups, AcceptedTranslator = true,
        });
        var warning = Render<TranslationAgreementWarning>();
        var page = Render<TranslationAgreement>();

        Assert.Single(warning.FindAll(".alert-warning"));
        Assert.True(page.Find("button").HasAttribute("disabled"));
        Assert.Contains("THE SOFTWARE IS PROVIDED", page.Markup);
        Assert.Equal("https://wiki.revolutionarygamesstudio.com/wiki/Translation_Guidelines",
            page.Find("a").GetAttribute("href"));

        page.Find("#translator-agreement").Change(true);
        Assert.False(page.Find("button").HasAttribute("disabled"));
        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Contains("You have already agreed", page.Markup));
        Assert.True(userInfo.AcceptedTranslator);
        warning.WaitForAssertion(() => Assert.Empty(warning.FindAll(".alert-warning")));

        var revisited = Render<TranslationAgreement>();
        Assert.Contains("You have already agreed", revisited.Markup);
        Assert.Empty(revisited.FindAll("#translator-agreement"));
    }
}
