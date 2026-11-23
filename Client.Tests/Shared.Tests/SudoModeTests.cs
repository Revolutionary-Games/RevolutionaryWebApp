namespace RevolutionaryWebApp.Client.Tests.Shared.Tests;

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Blazored.Modal;
using Bunit;
using Client;
using Microsoft.Extensions.DependencyInjection;
using RevolutionaryWebApp.Shared;
using RevolutionaryWebApp.Shared.Models;
using RichardSzalay.MockHttp;
using Services;
using Xunit;

public sealed class SudoModeTests : BunitContext, IAsyncLifetime
{
    private readonly MockHttpMessageHandler http = new();

    public Task InitializeAsync()
    {
        Services.AddBlazoredModal();
        Services.AddScoped<SudoModeService>();
        Services.AddScoped(sp => new HttpClient(new SudoModeHandler(sp.GetRequiredService<SudoModeService>())
        {
            InnerHandler = http,
        })
        {
            BaseAddress = new Uri("http://localhost/"),
            DefaultRequestHeaders = { { "X-CSRF-Token", "csrf" } },
        });
        JSInterop.Mode = JSRuntimeMode.Loose;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task SuccessfulChallengeRetriesOriginalMethodBodyAndHeadersOnce()
    {
        http.Expect(HttpMethod.Post, "/dangerous").WithContent("{\"reason\":\"requested\"}")
            .Respond(_ => Challenge());
        http.Expect(HttpMethod.Post, "/api/v1/SudoMode").WithContent("{\"code\":\"123456\"}")
            .WithHeaders("X-CSRF-Token", "csrf").Respond(HttpStatusCode.OK);
        http.Expect(HttpMethod.Post, "/dangerous").WithContent("{\"reason\":\"requested\"}")
            .WithHeaders("X-CSRF-Token", "csrf").Respond(HttpStatusCode.NoContent);
        var host = Host();
        Task<HttpResponseMessage> pending = null!;
        await host.InvokeAsync(() =>
        {
            pending = Services.GetRequiredService<HttpClient>().PostAsJsonAsync("dangerous",
                new { reason = "requested" });
        });
        host.WaitForAssertion(() => Assert.Single(host.FindAll("#sudo-mode-code")));
        Assert.False(pending.IsCompleted);
        host.Find("#sudo-mode-code").Input("123456");
        await host.InvokeAsync(() => Button(host, "Continue").Click());

        using var response = await pending;
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        http.VerifyNoOutstandingExpectation();
        Assert.Empty(host.FindAll("#sudo-mode-code"));
    }

    [Fact]
    public async Task CancelDoesNotAuthenticateOrRetryAndLaterRequestCanPromptAgain()
    {
        var target = http.When(HttpMethod.Delete, "/dangerous").Respond(_ => Challenge());
        var host = Host();
        for (int i = 0; i < 2; ++i)
        {
            Task<HttpResponseMessage> pending = null!;
            await host.InvokeAsync(() =>
            {
                pending = Services.GetRequiredService<HttpClient>().DeleteAsync("dangerous");
            });
            host.WaitForAssertion(() => Assert.Single(host.FindAll("#sudo-mode-code")));
            await host.InvokeAsync(() => Button(host, "Cancel").Click());
            using var response = await pending;
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.True((await response.Content.ReadFromJsonAsync<SudoModeRequiredDTO>())!.TwoFactorEnabled);
        }

        Assert.Equal(2, http.GetMatchCount(target));
    }

    [Fact]
    public async Task InvalidCodeKeepsPromptOpenWithoutRetryingOperation()
    {
        var target = http.When(HttpMethod.Delete, "/dangerous").Respond(_ => Challenge());
        http.When(HttpMethod.Post, "/api/v1/SudoMode").Respond(HttpStatusCode.BadRequest, "text/plain", "Invalid code");
        var host = Host();
        Task<HttpResponseMessage> pending = null!;
        await host.InvokeAsync(() => { pending = Services.GetRequiredService<HttpClient>().DeleteAsync("dangerous"); });
        host.WaitForAssertion(() => Assert.Single(host.FindAll("#sudo-mode-code")));
        host.Find("#sudo-mode-code").Input("123456");
        await host.InvokeAsync(() => Button(host, "Continue").Click());
        host.WaitForAssertion(() => Assert.Contains("Invalid code", host.Markup));
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, http.GetMatchCount(target));

        await host.InvokeAsync(() => Button(host, "Cancel").Click());
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task WithoutTwoFactorShowsSetupLinkAndDisablesContinue()
    {
        http.When(HttpMethod.Delete, "/dangerous").Respond(_ => Challenge(false));
        var host = Host();
        Task<HttpResponseMessage> pending = null!;
        await host.InvokeAsync(() => { pending = Services.GetRequiredService<HttpClient>().DeleteAsync("dangerous"); });
        host.WaitForAssertion(() => Assert.Single(host.FindAll("a[href='/me']")));
        Assert.Empty(host.FindAll("#sudo-mode-code"));
        Assert.True(Button(host, "Continue").HasAttribute("disabled"));
        await host.InvokeAsync(() => Button(host, "Cancel").Click());
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentRequestsShareOnePromptAndBothRetry()
    {
        var count = 0;
        http.When(HttpMethod.Delete, "/dangerous").Respond(_ =>
            Interlocked.Increment(ref count) <= 2 ? Challenge() : new HttpResponseMessage(HttpStatusCode.NoContent));
        http.When(HttpMethod.Post, "/api/v1/SudoMode").Respond(HttpStatusCode.OK);
        var host = Host();
        Task<HttpResponseMessage> first = null!;
        Task<HttpResponseMessage> second = null!;
        await host.InvokeAsync(() =>
        {
            var client = Services.GetRequiredService<HttpClient>();
            first = client.DeleteAsync("dangerous");
            second = client.DeleteAsync("dangerous");
        });
        host.WaitForAssertion(() => Assert.Single(host.FindAll("#sudo-mode-code")));
        host.Find("#sudo-mode-code").Input("123456");
        await host.InvokeAsync(() => Button(host, "Continue").Click());

        using var firstResponse = await first;
        using var secondResponse = await second;
        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondResponse.StatusCode);
        Assert.Equal(4, count);
    }

    [Fact]
    public async Task ARepeatedSudoRejectionDoesNotLoop()
    {
        var target = http.When(HttpMethod.Delete, "/dangerous").Respond(_ => Challenge());
        http.When(HttpMethod.Post, "/api/v1/SudoMode").Respond(HttpStatusCode.OK);
        var host = Host();
        Task<HttpResponseMessage> pending = null!;
        await host.InvokeAsync(() => { pending = Services.GetRequiredService<HttpClient>().DeleteAsync("dangerous"); });
        host.WaitForAssertion(() => Assert.Single(host.FindAll("#sudo-mode-code")));
        host.Find("#sudo-mode-code").Input("123456");
        await host.InvokeAsync(() => Button(host, "Continue").Click());
        using var response = await pending;
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(2, http.GetMatchCount(target));
        Assert.Empty(host.FindAll("#sudo-mode-code"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task OrdinaryErrorsPassThroughWithoutPromptOrRetry(HttpStatusCode status)
    {
        var target = http.When(HttpMethod.Delete, "/dangerous").Respond(status);
        var host = Host();
        using var response = await Services.GetRequiredService<HttpClient>().DeleteAsync("dangerous");
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, http.GetMatchCount(target));
        Assert.Empty(host.FindAll("#sudo-mode-code"));
    }

    [Fact]
    public async Task CancelledHttpRequestIsNotRetriedAfterSuccessfulChallenge()
    {
        var target = http.When(HttpMethod.Delete, "/dangerous").Respond(_ => Challenge());
        http.When(HttpMethod.Post, "/api/v1/SudoMode").Respond(HttpStatusCode.OK);
        var host = Host();
        using var cancellation = new CancellationTokenSource();
        Task<HttpResponseMessage> pending = null!;
        await host.InvokeAsync(() =>
        {
            pending = Services.GetRequiredService<HttpClient>().DeleteAsync("dangerous", cancellation.Token);
        });
        host.WaitForAssertion(() => Assert.Single(host.FindAll("#sudo-mode-code")));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        host.Find("#sudo-mode-code").Input("123456");
        await host.InvokeAsync(() => Button(host, "Continue").Click());
        Assert.Equal(1, http.GetMatchCount(target));
    }

    Task IAsyncLifetime.DisposeAsync()
    {
        return DisposeAsync().AsTask();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await base.DisposeAsyncCore();
        http.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            http.Dispose();

        base.Dispose(disposing);
    }

    private static HttpResponseMessage Challenge(bool enabled = true)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new SudoModeRequiredDTO { TwoFactorEnabled = enabled }),
        };
        response.Headers.Add(AppInfo.SudoModeResponseHeader, AppInfo.SudoModeRequiredHeaderValue);
        return response;
    }

    private static AngleSharp.Dom.IElement Button(IRenderedComponent<CascadingBlazoredModal> host, string text)
    {
        return host.FindAll("button").Single(b => b.TextContent.Trim() == text);
    }

    private IRenderedComponent<CascadingBlazoredModal> Host()
    {
        return Render<CascadingBlazoredModal>(p => p.Add(m => m.UseCustomLayout, true));
    }
}
