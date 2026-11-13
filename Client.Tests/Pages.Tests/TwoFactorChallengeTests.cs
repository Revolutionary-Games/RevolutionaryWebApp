namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System;
using Bunit;
using Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using RevolutionaryWebApp.Shared.Models;
using RichardSzalay.MockHttp;
using Xunit;

public class TwoFactorChallengeTests : BunitContext
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChallengeIncludesCsrfAndShowsPendingRecovery(bool recovery)
    {
        var csrf = CSRFMockFactory.Create();
        Services.AddSingleton(csrf);
        var http = Services.AddMockHttpClient();
        http.When("/LoginController/two-factor").RespondJson(new TwoFactorStatusDTO
        {
            Enabled = true,
            RecoveryAvailableAt = recovery ? DateTime.UtcNow.AddHours(72) : null,
        });

        var component = Render<TwoFactorChallenge>();

        component.WaitForAssertion(() => Assert.Single(component.FindAll("#two-factor-login-code")));
        Assert.Equal(csrf.Token, component.Find("form input[name='CSRF']").GetAttribute("value"));
        Assert.Equal("/LoginController/two-factor", component.Find("form").GetAttribute("action"));
        Assert.Equal(recovery ? 1 : 2, component.FindAll("form").Count);
        Assert.Contains(recovery ? "Removal of two-factor authentication has been requested" :
            "72-hour waiting period", component.Markup);
    }
}
