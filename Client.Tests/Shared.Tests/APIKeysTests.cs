namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Bunit;
using Client.Shared;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Mocks;
using RevolutionaryWebApp.Shared.Models;
using RevolutionaryWebApp.Shared.Models.Enums;
using RichardSzalay.MockHttp;
using Services;
using Xunit;

public sealed class APIKeysTests : BunitContext, IAsyncLifetime
{
    private readonly UserDTO user = new() { Id = 1, Groups = new CachedUserGroups(GroupType.User) };

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateFormOpensWithButtonCancelsAndShowsSecretOnceCreated()
    {
        var http = PrepareServices();
        http.When(HttpMethod.Get, "/api/v1/APIKeys/1").RespondJson(new APIKeyDTO[0]);
        var create = http.When(HttpMethod.Post, "/api/v1/APIKeys/1")
            .RespondJson(new CreatedAPIKey { Authorization = "key:created:secret" });
        var component = Render<APIKeys>(parameters => parameters
            .Add(page => page.User, user).Add(page => page.LookingAtSelf, true));
        component.WaitForAssertion(() => Assert.False(component.FindAll("button")
            .Single(button => button.TextContent.Trim() == "New API key").HasAttribute("disabled")));
        Assert.Empty(component.FindAll("form"));
        await component.InvokeAsync(() => component.FindAll("button")
            .Single(button => button.TextContent.Trim() == "New API key").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        component.Find("input[id^=network-]").Change("192.0.2.0/24");
        await component.InvokeAsync(() => component.FindAll("button")
            .Single(button => button.TextContent.Trim() == "Cancel").Click());
        Assert.Empty(component.FindAll("form"));
        await component.InvokeAsync(() => component.FindAll("button")
            .Single(button => button.TextContent.Trim() == "New API key").Click());
        component.WaitForAssertion(() => Assert.Single(component.FindAll("form")));
        Assert.Equal(string.Empty, component.Find("input[id^=network-]").GetAttribute("value") ?? string.Empty);
        await component.InvokeAsync(() => component.Find("form").Submit());
        component.WaitForAssertion(() => Assert.Contains("key:created:secret", component.Markup));
        Assert.Equal(1, http.GetMatchCount(create));
        Assert.Empty(component.FindAll("form"));
        component.Find(".alert-success .btn-close").Click();
        Assert.DoesNotContain("key:created:secret", component.Markup);
    }

    [Fact]
    public async Task SortingFetchesRequestedColumnAndDirectionFromServer()
    {
        var http = PrepareServices();
        var created = http.When(HttpMethod.Get, "/api/v1/APIKeys/1")
            .WithQueryString("sortColumn", "CreatedAt").WithQueryString("sortDirection", "Descending")
            .RespondJson(new[] { new APIKeyDTO { Id = "first" } });
        var lastUse = http.When(HttpMethod.Get, "/api/v1/APIKeys/1")
            .WithQueryString("sortColumn", "LastUsedAt").WithQueryString("sortDirection", "Descending")
            .RespondJson(new[] { new APIKeyDTO { Id = "second" } });
        var ascending = http.When(HttpMethod.Get, "/api/v1/APIKeys/1")
            .WithQueryString("sortColumn", "LastUsedAt").WithQueryString("sortDirection", "Ascending")
            .RespondJson(new[] { new APIKeyDTO { Id = "third" } });
        var component = Render<APIKeys>(parameters => parameters.Add(page => page.User, user));
        component.WaitForAssertion(() => Assert.Contains("first", component.Find("tbody").TextContent));
        Assert.Equal(1, http.GetMatchCount(created));
        await component.InvokeAsync(() => component.FindAll("th.sort")[1].Click());
        component.WaitForAssertion(() => Assert.Contains("second", component.Find("tbody").TextContent));
        Assert.Equal(1, http.GetMatchCount(lastUse));
        await component.InvokeAsync(() => component.FindAll("th.sort")[1].Click());
        component.WaitForAssertion(() => Assert.Contains("third", component.Find("tbody").TextContent));
        Assert.Equal(1, http.GetMatchCount(ascending));
    }

    [Fact]
    public void AllScopesDisablesOtherChoicesAndRestoresSelectionsWhenUnchecked()
    {
        var settings = new APIKeySettings { Scopes = [APIKeyScope.Builds, APIKeyScope.Users] };
        var component = Render<APIKeyEditor>(parameters => parameters
            .Add(editor => editor.EditedData, settings)
            .Add(editor => editor.EditContext, new EditContext(settings))
            .Add(editor => editor.ControlIdSuffix, "scopes"));
        component.Find("fieldset input").Change(true);
        Assert.All(component.FindAll("fieldset input").Skip(1), input =>
        {
            Assert.True(input.HasAttribute("disabled"));
            Assert.True(input.HasAttribute("checked"));
        });
        component.Find("fieldset input").Change(false);
        Assert.Equal(new[] { APIKeyScope.Builds, APIKeyScope.Users }, settings.Scopes);
        Assert.All(component.FindAll("fieldset input").Skip(1), input => Assert.False(input.HasAttribute("disabled")));
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return DisposeAsync().AsTask();
    }

    private MockHttpMessageHandler PrepareServices()
    {
        Services.AddSingleton<CurrentUserInfo>();
        Services.AddSingleton(CSRFMockFactory.Create());
        Services.AddSingleton<NotificationHandler>();
        return Services.AddMockHttpClient();
    }
}
