namespace AutomatedUITests.Tests;

using System;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.Playwright;
using RevolutionaryWebApp.Server;
using Xunit;

public class SiteNavigationTests : IClassFixture<WebHostServerFixture<Startup>>, IClassFixture<WebDriverFixture>
{
    private readonly WebDriverFixture driver;
    private readonly WebHostServerFixture server;

    public SiteNavigationTests(WebHostServerFixture<Startup> server, WebDriverFixture driver)
    {
        this.driver = driver;
        this.server = server;
    }

    [Theory]
    [InlineData("sk")]
    [InlineData("fi")]
    public async Task Navigation_DictionaryKeepsLinkedLanguageWhenLanguagesLoad(string language)
    {
        var page = await driver.NewPage(server.RootUri);
        await driver.WaitUntilBlazorIsLoaded(page);

        var releaseLanguages = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/v1/translationDictionaries/1/languages", async route =>
        {
            await releaseLanguages.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = "[\"aeb\",\"fi\",\"sk\"]",
            });
        });
        await page.RouteAsync("**/api/v1/translationDictionaries/1/terms?*", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = "{\"results\":[],\"currentPage\":1,\"pageCount\":1}",
            }));

        try
        {
            await page.GotoAsync(new Uri(server.RootUri,
                $"translations/dictionaries/1?language={language}&search=Launcher").ToString());
            var selector = page.Locator("#dictionary-language");
            await Assertions.Expect(selector).ToHaveValueAsync(language);

            releaseLanguages.SetResult();
            await Assertions.Expect(selector.Locator("option[value='aeb']")).ToHaveCountAsync(1);
            await Assertions.Expect(selector).ToHaveValueAsync(language);
            await Assertions.Expect(page.Locator("#dictionary-search")).ToHaveValueAsync("Launcher");
        }
        finally
        {
            releaseLanguages.TrySetResult();
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task Navigation_MainPageLoadsAndCanNavigate()
    {
        var root = server.RootUri;

        var page = await driver.NewPage(root);

        await driver.WaitUntilBlazorIsLoaded(page);

        // Main page
        Assert.Equal("Revolutionary Games Web App", await page.TextContentAsync(".content h1"));

        // About page
        await page.ClickAsync("#topAboutLink");

        Assert.Equal("About", await page.TextContentAsync(".content h3"));

        // DevBuilds
        await page.ClickAsync("#leftDevBuildButton");

        // Files
        await page.ClickAsync("#leftFilesButton");

        // Git LFS
        await page.ClickAsync("#leftLFSButton");

        await driver.WaitUntilSpinnersGoAway(page);

        // When not an admin this button is not visible
        Assert.Empty(await page.QuerySelectorAllAsync("#leftUsersButton"));

        // Login page
        await page.ClickAsync("#userWidgetLoginLink");

        Assert.Equal("Login", await page.TextContentAsync(".content h3"));

        await driver.WaitUntilSpinnersGoAway(page);

        // Login button and the fields for local login exist

        Assert.NotNull(await page.QuerySelectorAsync("#localLoginButton"));

        await page.FillAsync("input[type=email]", "test@example.com");
        Assert.NotNull(await page.QuerySelectorAsync("input[type=password]"));
    }
}
