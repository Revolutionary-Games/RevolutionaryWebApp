namespace AutomatedUITests.Fixtures
{
    using System;
    using OpenQA.Selenium;
    using OpenQA.Selenium.Chrome;

    public class WebDriverFixture : IDisposable
    {
        public WebDriverFixture()
        {
            Driver = new ChromeDriver();
        }

        public IWebDriver Driver { get; }

        public void Dispose()
        {
            Driver.Quit();
            Driver.Dispose();
        }
    }
}
