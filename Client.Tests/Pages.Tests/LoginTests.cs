namespace ThriveDevCenter.Client.Tests.Pages.Tests;

using System.Collections.Generic;
using Bunit;
using Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using RichardSzalay.MockHttp;
using ThriveDevCenter.Shared.Models;
using Utilities;
using Xunit;

public class LoginTests : TestContext
{
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
        var cut = RenderComponent<Login>();

        cut.WaitForAssertion(() => cut.FindIsNull(".spinner-border"));

        cut.FindIsNull("form button");
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
        var cut = RenderComponent<Login>();

        cut.WaitForAssertion(() => cut.FindIsNull(".spinner-border"));

        Assert.Contains(cut.Find("form button").Attributes, i => i.Name == "disabled");

        cut.Find("input[type=email]").Input("test@example.com");
        cut.Find("input[type=password]").Input("12345");

        Assert.DoesNotContain(cut.Find("form button").Attributes, i => i.Name == "disabled");
    }
}
