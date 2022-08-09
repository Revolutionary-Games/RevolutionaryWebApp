namespace AutomatedUITests.Fixtures;

using System;
using System.Threading.Tasks;
using Microsoft.Playwright;

public class WebDriverFixture : IDisposable
{
    private static readonly bool Headless = true;

    private readonly IPlaywright playwright;
    private readonly Task<IBrowser> browser;

    public WebDriverFixture()
    {
        playwright = Playwright.CreateAsync().Result;

        var options = new BrowserTypeLaunchOptions
        {
            Headless = Headless,
        };

        browser = playwright.Chromium.LaunchAsync(options);
    }

    private IBrowser Browser => browser.Result;

    public async Task<IPage> NewPage(Uri address)
    {
        var page = await Browser.NewPageAsync();
        await page.GotoAsync(address.ToString());
        return page;
    }

    /// <summary>
    ///   Waits until the app is loaded and the main "page" body div has appeared
    /// </summary>
    /// <param name="page">The page to use</param>
    public async Task WaitUntilBlazorIsLoaded(IPage page)
    {
        var mainContentLocator = page.Locator(".page");
        await mainContentLocator.WaitForAsync();
    }

    public async Task WaitUntilSpinnersGoAway(IPage page)
    {
        var mainContentLocator = page.Locator(".spinner-border");

        var start = DateTime.Now;

        while (await mainContentLocator.CountAsync() > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(50));

            if (DateTime.Now - start > TimeSpan.FromSeconds(30))
                throw new Exception("Spinner did not disappear within time limit");
        }
    }

    public async void Dispose()
    {
        await Browser.DisposeAsync();
        playwright.Dispose();
    }
}