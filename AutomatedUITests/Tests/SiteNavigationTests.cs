namespace AutomatedUITests.Tests
{
    using System;
    using Fixtures;
    using OpenQA.Selenium;
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

            driver.WaitUntilBlazorIsLoaded();

            // Main page
            Assert.Equal("ThriveDevCenter", driver.Driver.FindElement(By.CssSelector(".content h1")).Text);

            // About page
            driver.Driver.FindElement(By.CssSelector("#topAboutLink")).Click();

            Assert.Equal("About", driver.Driver.FindElement(By.CssSelector(".content h3")).Text);

            // DevBuilds
            driver.Driver.FindElement(By.CssSelector("#leftDevBuildButton")).Click();

            // Files
            driver.Driver.FindElement(By.CssSelector("#leftFilesButton")).Click();

            // Git LFS
            driver.Driver.FindElement(By.CssSelector("#leftLFSButton")).Click();

            // Wait until the spinner goes away
            var wait = new WebDriverWait(driver.Driver, TimeSpan.FromSeconds(10));
            wait.Until((d) => ElementHelpers.ElementDoesNotExist(d, "spinner-border"));

            // When not an admin this button is not visible
            Assert.Empty(driver.Driver.FindElements(By.CssSelector("#leftUsersButton")));

            // Login page
            driver.Driver.FindElement(By.CssSelector("#userWidgetLoginLink")).Click();

            Assert.Equal("Login", driver.Driver.FindElement(By.CssSelector(".content h3")).Text);

            // Wait until the spinner goes away
            wait = new WebDriverWait(driver.Driver, TimeSpan.FromSeconds(10));
            wait.Until((d) => ElementHelpers.ElementDoesNotExist(d, "spinner-border"));

            // Login button and the fields for local login exist
            driver.Driver.FindElement(By.CssSelector("#localLoginButton"));
            driver.Driver.FindElement(By.CssSelector("input[type=email]")).SendKeys("test@example.com");
            driver.Driver.FindElement(By.CssSelector("input[type=password]"));
        }
    }
}
