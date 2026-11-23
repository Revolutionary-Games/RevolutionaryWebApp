namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System.Collections.Generic;
using AngleSharp.Dom;
using Bunit;
using Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using RevolutionaryWebApp.Shared.Models;
using RichardSzalay.MockHttp;
using Utilities;
using Xunit;

public class LoginTests : BunitContext
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GithubLoginRendersProviderButton(bool active)
    {
        var mockCSRF = CSRFMockFactory.Create();
        var http = Services.AddMockHttpClient();
        http.When("/api/v1/Registration").RespondJson(false);
        http.When("/LoginController").RespondJson(new LoginOptions
        {
            Categories = new List<LoginCategory>
            {
                new()
                {
                    Name = "SSO Login",
                    Options = new List<LoginOption>
                    {
                        new()
                        {
                            ReadableName = "Login Using GitHub",
                            InternalName = "github",
                            Active = active,
                        },
                    },
                },
            },
        });
        Services.AddSingleton(mockCSRF);

        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.FindIsNull(".spinner-border"));
        var form = cut.Nodes.QuerySelector("form[action='/LoginController/start']")!;
        Assert.Equal("github", form.QuerySelector("input[name='SsoType']")!.GetAttribute("value"));
        Assert.Equal(mockCSRF.Token, form.QuerySelector("input[name='CSRF']")!.GetAttribute("value"));
        var button = form.QuerySelector("button")!;
        Assert.Equal("Login Using GitHub", button.TextContent.Trim());
        Assert.Equal(!active, button.HasAttribute("disabled"));
    }

    [Fact]
    public void LoginLocal_IsNotRenderedWhenDisabled()
    {
        var mockCSRF = CSRFMockFactory.Create();

        var http = Services.AddMockHttpClient();
        http.When("/api/v1/Registration").RespondJson(false);
        http.When("/LoginController").RespondJson(new LoginOptions
        {
            Categories = new List<LoginCategory>
            {
                new()
                {
                    Name = "Local Account",
                    Options = new List<LoginOption>
                    {
                        new()
                        {
                            ReadableName = "Login using a local account",
                            InternalName = "local",
                            Active = false,
                            Local = true,
                        },
                    },
                },
            },
        });

        Services.AddSingleton(mockCSRF);
        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.FindIsNull(".spinner-border"));

        // Ensure local login button isn't rendered (signup form may still exist)
        cut.FindIsNull("#localLoginButton");
    }

    [Fact]
    public void LoginLocal_LoginButtonEnablesWhenTextInput()
    {
        var mockCSRF = CSRFMockFactory.Create();

        var http = Services.AddMockHttpClient();
        http.When("/api/v1/Registration").RespondJson(false);
        http.When("/LoginController").RespondJson(new LoginOptions
        {
            Categories = new List<LoginCategory>
            {
                new()
                {
                    Name = "Local Account",
                    Options = new List<LoginOption>
                    {
                        new()
                        {
                            ReadableName = "Login using a local account",
                            InternalName = "local",
                            Active = true,
                            Local = true,
                        },
                    },
                },
            },
        });

        Services.AddSingleton(mockCSRF);
        var cut = Render<Login>();

        cut.WaitForAssertion(() => cut.FindIsNull(".spinner-border"));

        // Need to use a non-wrapped access to the nodes for now
        // See: https://github.com/bUnit-dev/bUnit/issues/1262
        Assert.NotNull(cut.Nodes.QuerySelector("#localLoginButton"));
        Assert.Contains(cut.Nodes.QuerySelector("#localLoginButton")!.Attributes, i => i.Name == "disabled");

        Assert.NotNull(cut.Nodes.QuerySelector("input[type=email]"));
        cut.Nodes.QuerySelector("input[type=email]")!.Input("test@example.com");

        Assert.NotNull(cut.Nodes.QuerySelector("input[type=password]"));
        cut.Nodes.QuerySelector("input[type=password]")!.Input("12345");

        Assert.DoesNotContain(cut.Nodes.QuerySelector("#localLoginButton")!.Attributes, i => i.Name == "disabled");
    }
}
