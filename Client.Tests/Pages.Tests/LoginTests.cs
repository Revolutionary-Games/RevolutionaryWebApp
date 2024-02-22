namespace RevolutionaryWebApp.Client.Tests.Pages.Tests;

using System.Collections.Generic;
using AngleSharp.Dom;
using Bunit;
using Client.Pages;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using RichardSzalay.MockHttp;
using RevolutionaryWebApp.Shared.Models;
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

        // Need to use a non-wrapped access to the nodes for now
        // See: https://github.com/bUnit-dev/bUnit/issues/1262
        Assert.NotNull(cut.Nodes.QuerySelector("form button"));
        Assert.Contains(cut.Nodes.QuerySelector("form button")!.Attributes, i => i.Name == "disabled");

        Assert.NotNull(cut.Nodes.QuerySelector("input[type=email]"));
        cut.Nodes.QuerySelector("input[type=email]")!.Input("test@example.com");

        Assert.NotNull(cut.Nodes.QuerySelector("input[type=password]"));
        cut.Nodes.QuerySelector("input[type=password]")!.Input("12345");

        Assert.DoesNotContain(cut.Nodes.QuerySelector("form button")!.Attributes, i => i.Name == "disabled");
    }
}
