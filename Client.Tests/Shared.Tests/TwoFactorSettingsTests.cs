namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bunit;
using Client.Shared;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using RevolutionaryWebApp.Shared.Models;
using RichardSzalay.MockHttp;
using Services;
using Xunit;

public class TwoFactorSettingsTests : BunitContext
{
    [Fact]
    public async Task SetupShowsQrThenPrintableBackupCodesAndClearsSecretsAfterSaving()
    {
        var user = new CurrentUserInfo();
        user.OnReceivedOurInfo(new UserDTO { Email = "twofactor@example.com" });
        Services.AddSingleton(user);
        var http = Services.AddMockHttpClient();
        http.When(HttpMethod.Get, "/api/v1/TwoFactor").RespondJson(new TwoFactorStatusDTO());
        http.When(HttpMethod.Post, "/api/v1/TwoFactor/setup").RespondJson(new TwoFactorSetupDTO
        {
            Secret = "SETUPSECRET",
            QrCodeDataUrl = "data:image/png;base64,iVBOR",
        });
        var enable = http.When(HttpMethod.Post, "/api/v1/TwoFactor/enable")
            .RespondJson(new List<string> { "FIRST-BACKUP-CODE", "SECOND-BACKUP-CODE" });
        var print = JSInterop.SetupVoid("printTwoFactorBackupCodes", _ => true);
        print.SetVoidResult();
        var component = Render<TwoFactorSettings>();
        component.WaitForAssertion(() => Assert.Contains("Set up authenticator", component.Markup));

        await component.InvokeAsync(() => component.Find("button").Click());
        component.WaitForAssertion(() => Assert.Equal("data:image/png;base64,iVBOR",
            component.Find("img").GetAttribute("src")));
        Assert.Contains("SETUPSECRET", component.Markup);
        component.Find("#two-factor-setup-code").Input("123456");
        await component.InvokeAsync(() => component.FindAll("button")
            .Single(b => b.TextContent.Trim() == "Enable two-factor authentication").Click());
        component.WaitForAssertion(() => Assert.Contains("FIRST-BACKUP-CODE", component.Markup));
        Assert.Equal(1, http.GetMatchCount(enable));
        Assert.DoesNotContain("SETUPSECRET", component.Markup);
        Assert.Empty(component.FindAll("img"));
        await component.InvokeAsync(() => component.FindAll("button")
            .Single(b => b.TextContent.Trim() == "Print backup codes").Click());
        Assert.Single(print.Invocations);
        await component.InvokeAsync(() => component.FindAll("button")
            .Single(b => b.TextContent.Trim() == "I have saved these codes").Click());
        Assert.DoesNotContain("FIRST-BACKUP-CODE", component.Markup);
        Assert.Contains("Backup codes remaining: 2", component.Markup);
    }
}
