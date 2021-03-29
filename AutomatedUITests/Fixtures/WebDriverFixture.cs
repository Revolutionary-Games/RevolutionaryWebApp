namespace AutomatedUITests.Fixtures
{
    using System;
    using OpenQA.Selenium;
    using OpenQA.Selenium.Chrome;

    public class WebDriverFixture : IDisposable
    {
        private static readonly bool Headless = false;

        public WebDriverFixture()
        {
            var options = new ChromeOptions();

            if (Headless)
            {
                options.AddArgument("--headless");
            }

            Driver = new ChromeDriver(options);
        }

        public IWebDriver Driver { get; }

        public void Dispose()
        {
            Driver.Quit();
            Driver.Dispose();
        }
    }
}
