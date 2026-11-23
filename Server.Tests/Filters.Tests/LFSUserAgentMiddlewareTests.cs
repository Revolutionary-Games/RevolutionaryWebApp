namespace RevolutionaryWebApp.Server.Tests.Filters.Tests;

using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Server.Filters;
using Xunit;

public class LFSUserAgentMiddlewareTests
{
    [Theory]
    [InlineData("/api/v1/lfs/project", "Mozilla/5.0 (compatible; FacebookExternalHit/1.1)")]
    [InlineData("/api/v1/download_lfs", "prefix-facebookexternalhit-suffix")]
    public async Task Middleware_BlocksFacebookExternalHitOnLfsEndpoints(string path, string userAgent)
    {
        using var host = await CreateHost();
        var request = host.GetTestServer().CreateRequest(path).AddHeader("User-Agent", userAgent);

        using var response = await request.GetAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("Facebook is downloading way more data than is reasonable",
            await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Middleware_BlocksWhenUserAgentIsInAnotherHeaderValue()
    {
        using var host = await CreateHost();
        var request = host.GetTestServer().CreateRequest("/api/v1/lfs/project")
            .AddHeader("User-Agent", "Mozilla/5.0")
            .AddHeader("User-Agent", "facebookexternalhit/1.1");

        using var response = await request.GetAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/lfs/project", "Mozilla/5.0")]
    [InlineData("/api/v1/lfs/project", null)]
    [InlineData("/api/v1/lfs-extra/project", "facebookexternalhit/1.1")]
    [InlineData("/api/v1/download_lfs_extra", "facebookexternalhit/1.1")]
    [InlineData("/api/v1/other", "facebookexternalhit/1.1")]
    public async Task Middleware_AllowsOtherRequests(string path, string? userAgent)
    {
        using var host = await CreateHost();
        var request = host.GetTestServer().CreateRequest(path);

        if (userAgent != null)
        {
            request.AddHeader("User-Agent", userAgent);
        }

        using var response = await request.GetAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<IHost> CreateHost()
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer().Configure(app =>
                {
                    app.UseMiddleware<LFSUserAgentMiddleware>();
                    app.Run(context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status204NoContent;
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync();
    }
}
