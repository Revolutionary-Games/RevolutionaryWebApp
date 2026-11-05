namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System.Net.Http;
using System.Text.Json;
using Bunit;
using Client.Pages;
using Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using RichardSzalay.MockHttp;
using Services;
using Xunit;

public class TranslationAgreementTests : BunitContext
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void AgreementRequiresCheckboxAndUpdatesUserAndWarning(bool initialUseEmail, bool useEmail)
    {
        var http = Services.AddMockHttpClient();
        var userInfo = new CurrentUserInfo();
        var user = new UserDTO
        {
            Id = 1, Groups = new CachedUserGroups(GroupType.User), UseEmailInTranslationCredits = initialUseEmail,
        };
        userInfo.OnReceivedOurInfo(user);
        Services.AddSingleton(userInfo);
        http.Expect(HttpMethod.Post, "/api/v1/translationAgreement")
            .WithJsonContent<TranslationCreditSettingsDTO>(
                settings => settings.UseEmailInTranslationCredits == useEmail,
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .RespondJson(new UserDTO
            {
                Id = 1, Groups = user.Groups, AcceptedTranslator = true, UseEmailInTranslationCredits = useEmail,
            });
        var warning = Render<TranslationAgreementWarning>();
        var page = Render<TranslationAgreement>();

        Assert.Single(warning.FindAll(".alert-warning"));
        Assert.True(page.Find("button").HasAttribute("disabled"));
        Assert.Contains("THE SOFTWARE IS PROVIDED", page.Markup);
        Assert.Equal("https://wiki.revolutionarygamesstudio.com/wiki/Translation_Guidelines",
            page.Find("a").GetAttribute("href"));

        Assert.Equal(initialUseEmail, page.Find("#translator-credit-email").HasAttribute("checked"));
        Assert.Contains("public commit history", page.Markup);
        Assert.Equal("/me", page.Find("a[href='/me']").GetAttribute("href"));
        page.Find("#translator-credit-email").Change(useEmail);
        Assert.True(page.Find("button").HasAttribute("disabled"));
        page.Find("#translator-agreement").Change(true);
        Assert.False(page.Find("button").HasAttribute("disabled"));
        page.Find("button").Click();

        page.WaitForAssertion(() => Assert.Contains("You have already agreed", page.Markup));
        Assert.True(userInfo.AcceptedTranslator);
        Assert.Equal(useEmail, userInfo.Info!.UseEmailInTranslationCredits);
        http.VerifyNoOutstandingExpectation();
        warning.WaitForAssertion(() => Assert.Empty(warning.FindAll(".alert-warning")));

        var revisited = Render<TranslationAgreement>();
        Assert.Contains("You have already agreed", revisited.Markup);
        Assert.Empty(revisited.FindAll("#translator-agreement"));
    }
}
