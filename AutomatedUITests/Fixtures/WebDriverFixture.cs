namespace AutomatedUITests.Fixtures
{
    using System;
    using OpenQA.Selenium;
    using OpenQA.Selenium.Chrome;
    using OpenQA.Selenium.Support.UI;
    using Utilities;

    public class WebDriverFixture : IDisposable
    {
        private const int SiteLoadMaxWaitTime = 20;
        private static readonly bool Headless = true;

        public WebDriverFixture()
        {
            var options = new ChromeOptions();

            if (Headless)
                options.AddArgument("--headless");

            Driver = new ChromeDriver(options);
        }

        public IWebDriver Driver { get; }

        public void WaitUntilBlazorIsLoaded()
        {
            // Wait until the app is loaded and the main "page" body div has appeared
            var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(SiteLoadMaxWaitTime));
            wait.Until((d) => ElementHelpers.ElementExists(d, ".page"));
        }

        public void Dispose()
        {
            Driver.Quit();
            Driver.Dispose();
        }
    }
}
