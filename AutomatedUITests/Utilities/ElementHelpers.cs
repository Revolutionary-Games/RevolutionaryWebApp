namespace AutomatedUITests.Utilities
{
    using OpenQA.Selenium;

    public static class ElementHelpers
    {
        public static bool ElementExists(IWebDriver driver, string cssSelector)
        {
            // From https://stackoverflow.com/a/50604007/4371508
            try
            {
                var elementToBeDisplayed = driver.FindElement(By.CssSelector(cssSelector));
                return elementToBeDisplayed.Displayed;
            }
            catch (StaleElementReferenceException)
            {
                return false;
            }
            catch (NoSuchElementException)
            {
                return false;
            }
        }

        public static bool ElementDoesNotExist(IWebDriver driver, string cssSelector)
        {
            try
            {
                var elementToBeDisplayed = driver.FindElement(By.CssSelector(cssSelector));
                return !elementToBeDisplayed.Displayed;
            }
            catch (StaleElementReferenceException)
            {
                return false;
            }
            catch (NoSuchElementException)
            {
                return true;
            }
        }


    }
}
