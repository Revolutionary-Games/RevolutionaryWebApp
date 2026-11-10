namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System.Net;
using System.Net.Http;
using Bunit;
using Client.Pages;
using Client.Utilities;
using RevolutionaryWebApp.Shared.Models;
using RichardSzalay.MockHttp;
using Xunit;

public class TranslationEngageTests : BunitContext
{
    [Theory]
    [InlineData("pt-BR", "pt_BR")]
    [InlineData("PT-br", "pt_BR")]
    [InlineData("fi-FI", "fi")]
    [InlineData("de", null)]
    [InlineData("pt-PT", null)]
    public void MatchesExactLanguageOrParentWithoutChoosingAnotherRegion(string preferred, string? expected)
    {
        TranslationLanguageDTO[] languages =
        [
            new() { LanguageCode = "fi" }, new() { LanguageCode = "pt_BR" },
        ];
        Assert.Equal(expected, TranslationLanguageMatcher.FindMatch(languages, [preferred])?.LanguageCode);
    }

    [Fact]
    public void OffersPreferredSupportedLanguageAndAllLanguages()
    {
        var http = Services.AddMockHttpClient();
        http.Expect(HttpMethod.Get, "/api/v1/translations/projects/thrive")
            .RespondJson(new TranslationProjectDTO { Name = "Thrive", Slug = "thrive" });
        http.Expect(HttpMethod.Get, "/api/v1/translations/projects/thrive/languages")
            .RespondJson(new TranslationLanguageDTO[]
            {
                new() { LanguageCode = "fi", DisplayName = "Finnish", Total = 100, Translated = 20, Verified = 30 },
                new() { LanguageCode = "de", DisplayName = "German", Total = 100, Translated = 50 },
            });
        JSInterop.Setup<string[]>("getPreferredLanguages").SetResult(["fr-FR", "fi-FI", "de"]);

        var page = Render<TranslationEngage>(parameters => parameters.Add(p => p.Slug, "thrive"));

        page.WaitForAssertion(() =>
        {
            Assert.Contains("Thrive", page.Find("h1").TextContent);
            Assert.Contains("Translate into", page.Find("a[href='translations/thrive/fi']").TextContent);
            Assert.Contains("View project languages", page.Find("a[href='translations/thrive']").TextContent);
            Assert.Contains(0.5.ToString("P1"), page.Markup);
        });
        http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public void UnknownProjectShowsErrorWithoutInvitation()
    {
        var http = Services.AddMockHttpClient();
        http.Expect(HttpMethod.Get, "/api/v1/translations/projects/missing").Respond(HttpStatusCode.NotFound);
        JSInterop.Setup<string[]>("getPreferredLanguages").SetResult([]);

        var page = Render<TranslationEngage>(parameters => parameters.Add(p => p.Slug, "missing"));

        page.WaitForAssertion(() => Assert.Contains("could not be found", page.Find("[role='alert']").TextContent));
        Assert.Empty(page.FindAll("a.btn"));
        http.VerifyNoOutstandingExpectation();
    }
}
