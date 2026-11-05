namespace AutomatedUITests.Tests;

using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.Playwright;
using RevolutionaryWebApp.Shared.Models.Enums;
using Xunit;

public class LocalAccountTests : IClassFixture<LocalAccountServerFixture>, IClassFixture<WebDriverFixture>
{
    private readonly LocalAccountServerFixture server;
    private readonly WebDriverFixture driver;

    public LocalAccountTests(LocalAccountServerFixture server, WebDriverFixture driver)
    {
        this.server = server;
        this.driver = driver;
    }

    [Fact]
    public async Task LocalAccount_CanRegisterUsingEmailLinkAndLogInToOwnPage()
    {
        var username = "uitest" + Guid.NewGuid().ToString("N")[..12];
        var email = username + "@example.com";
        const string password = "UI test account password 123!";

        var page = await driver.NewPage(new Uri(server.RootUri, "login"));
        try
        {
            await driver.WaitUntilBlazorIsLoaded(page);

            // Scope to the signup form, as the login form also has an email input.
            var signupForm = page.Locator("form").Filter(new LocatorFilterOptions
            {
                Has = page.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }),
            });
            await signupForm.Locator("input[type=email]").FillAsync(email);
            await signupForm.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
            await Assertions.Expect(page.GetByText("Check your inbox for a link to complete signup.",
                new() { Exact = true })).ToBeVisibleAsync();

            var mail = await server.MailQueue.WaitForEmail();
            Assert.Equal(email, mail.Recipient);
            Assert.Equal(EmailReason.ConfirmEmail, mail.Category);
            Assert.NotNull(mail.HtmlBody);
            var link = Regex.Match(mail.HtmlBody, "href=\"([^\"]+/complete-signup/[^\"]+)\"");
            Assert.True(link.Success, "The confirmation email must contain a signup link");

            // BaseUrl uses a fixed port in test configuration; Kestrel chooses a free port.
            var signupLink = new Uri(link.Groups[1].Value);
            await page.GotoAsync(new Uri(server.RootUri, signupLink.PathAndQuery).ToString());
            await Assertions.Expect(page.Locator(".content strong")).ToHaveTextAsync(email);
            await page.Locator("#username").FillAsync(username);
            await page.Locator("#password").FillAsync(password);
            await page.Locator("#password2").FillAsync(password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Create Account", Exact = true }).ClickAsync();
            await Assertions.Expect(page.Locator("#userWidgetLoggedIn")).ToHaveTextAsync(username);

            // Signup automatically logs in. Log out to exercise password authentication independently.
            await page.Locator("form[action='/Logout'] button").ClickAsync();
            await page.Locator("#userWidgetLoginLink").ClickAsync();
            await page.Locator(".login-form input[type=email]").FillAsync(email);
            await page.Locator(".login-form input[type=password]").FillAsync(password);
            await page.Locator("#localLoginButton").ClickAsync();
            await Assertions.Expect(page.Locator("#userWidgetLoggedIn")).ToHaveTextAsync(username);

            await page.Locator("#userWidgetLoggedIn").ClickAsync();
            await Assertions.Expect(page).ToHaveURLAsync(new Uri(server.RootUri, "me").ToString());
            await Assertions.Expect(page.Locator(".content h3")).ToHaveTextAsync("Your Profile");
            await Assertions.Expect(page.Locator(".content h4").First).ToContainTextAsync(username);
            await Assertions.Expect(page.Locator(".content li").Filter(new() { HasText = "Email:" }))
                .ToHaveTextAsync("Email: " + email);
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }
}
