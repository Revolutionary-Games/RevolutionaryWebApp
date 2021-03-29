namespace AutomatedUITests.Tests
{
    using Fixtures;
    using ThriveDevCenter.Server;
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
        }
    }
}
