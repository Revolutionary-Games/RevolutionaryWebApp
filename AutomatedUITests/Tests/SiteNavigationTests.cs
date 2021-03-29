namespace AutomatedUITests.Tests
{
    using System;
    using Fixtures;
    using OpenQA.Selenium.Support.UI;
    using ThriveDevCenter.Server;
    using Utilities;
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
        public void Navigation_MainPageLoadsAndCanNavigate()
        {
            var root = server.RootUri;

            driver.Driver.Navigate().GoToUrl(root);

            // Wait until the app is loaded and the main "page" body div has appeared

            // var wait = new WebDriverWait(driver.Driver, TimeSpan.FromSeconds(10));
            var wait = new WebDriverWait(driver.Driver, TimeSpan.FromMinutes(1));

            wait.Until((d) => ElementHelpers.ElementExists(d, ".page"));
        }
    }
}
