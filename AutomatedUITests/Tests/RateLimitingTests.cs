namespace AutomatedUITests.Tests;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Fixtures;
using ThriveDevCenter.Server;
using Xunit;

/// <summary>
///   Tests rate limiting using real configuration
/// </summary>
public class RateLimitingTests : IClassFixture<WebHostServerFixture<Startup>>
{
    private readonly WebHostServerFixture server;

    public RateLimitingTests(WebHostServerFixture<Startup> server)
    {
        this.server = server;
    }

    // [Fact]
    public async void RateLimiting_OnLoginPage()
    {
        var root = server.RootUri;

        using var client = new HttpClient
        {
            BaseAddress = root,
        };

        // Set this to just get this one test IP rate limited
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "172.16.2.47");

        var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("Email", "123@example.com") });

        var response = await client.PostAsync("LoginController/login", content);

        // We send bad data kind of intentionally as it would be much harder to send right data
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // TODO: fix this test, probably fails due to rate limiting not being enabled in the test server
        // even though it really should be enabled
        /*
        for (int i = 0; i < 20; ++i)
        {
            response = await client.PostAsync("LoginController/login", content);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                // Assert.Equal();

                // Another IP should still work
                client.DefaultRequestHeaders.Remove("X-Forwarded-For");
                client.DefaultRequestHeaders.Add("X-Forwarded-For", "172.16.2.48");

                response = await client.PostAsync("LoginController/login", content);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

                return;
            }
        }

        Assert.Fail("Rate limit response was not received");
        */
    }
}
