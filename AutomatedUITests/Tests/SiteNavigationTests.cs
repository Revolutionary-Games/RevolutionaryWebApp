namespace AutomatedUITests.Tests;

using Fixtures;
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

    [Fact]
    public async void Navigation_MainPageLoadsAndCanNavigate()
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
