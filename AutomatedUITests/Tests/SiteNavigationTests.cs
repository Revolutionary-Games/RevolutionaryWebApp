namespace AutomatedUITests.Tests
{
    using System;
    using Fixtures;
    using OpenQA.Selenium.Support.UI;
    using Utilities;
    using Xunit;

    public class SiteNavigationTests : IClassFixture<WebDriverFixture>
    {
        private readonly WebDriverFixture driver;

        public SiteNavigationTests(WebDriverFixture driver)
        {
            this.driver = driver;
        }

        [Fact]
        public void Navigation_MainPageLoadsAndCanNavigate()
        {
            driver.Driver.Navigate().GoToUrl(ManualTestServerInstance.BaseUrl);

            // Wait until the app is loaded and the main "page" body div has appeared

            // var wait = new WebDriverWait(driver.Driver, TimeSpan.FromSeconds(10));
            var wait = new WebDriverWait(driver.Driver, TimeSpan.FromMinutes(1));

            wait.Until((d) => ElementHelpers.ElementExists(d, ".page"));
        }
    }
}
