namespace RevolutionaryWebApp.Server.Tests.Services.Tests;

using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Services;
using Xunit;

public class GithubOAuthClientTests
{
    [Theory]
    [InlineData("read:user,user:email")]
    [InlineData("user:email read:user")]
    public async Task ExchangesCodeAndFetchesAuthenticatedUserAndEmails(string scopes)
    {
        using var handler = new GithubHandler(scopes);
        using var httpClient = new HttpClient(handler);
        var api = CreateClient(httpClient);

        var (user, emails) = await api.GetUser("code", "https://example.com/OAuthReturn/github");

        Assert.Equal("githubuser", user.Login);
        Assert.Equal(123, user.Id);
        Assert.Equal("user@example.com", Assert.Single(emails).Email);
        Assert.Equal(3, handler.RequestCount);
        Assert.Contains("client_id=client", handler.TokenRequestBody);
        Assert.Contains("client_secret=secret", handler.TokenRequestBody);
        Assert.Contains("code=code", handler.TokenRequestBody);
        Assert.Contains("redirect_uri=https%3A%2F%2Fexample.com%2FOAuthReturn%2Fgithub", handler.TokenRequestBody);
    }

    [Theory]
    [InlineData("read:user")]
    [InlineData("user:email")]
    [InlineData("read:user,user:email:invalid")]
    public async Task RejectsMissingScopesBeforeFetchingUser(string scopes)
    {
        using var handler = new GithubHandler(scopes);
        using var httpClient = new HttpClient(handler);
        var api = CreateClient(httpClient);

        await Assert.ThrowsAsync<System.Exception>(() => api.GetUser("code", "https://example.com/callback"));

        Assert.Equal(1, handler.RequestCount);
    }

    private static GithubOAuthClient CreateClient(HttpClient client)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("github").Returns(client);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "Login:Github:ClientId", "client" },
            { "Login:Github:ClientSecret", "secret" },
        }).Build();
        return new GithubOAuthClient(factory, configuration);
    }

    private sealed class GithubHandler(string scopes) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public string? TokenRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ++RequestCount;
            string response;
            if (RequestCount == 1)
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://github.com/login/oauth/access_token", request.RequestUri!.ToString());
                Assert.Null(request.Headers.Authorization);
                TokenRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                response = $$"""
                             {"access_token":"token", "token_type":"bearer", "scope":"{{scopes}}"}
                             """;
            }
            else
            {
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                Assert.Equal("token", request.Headers.Authorization.Parameter);
                if (RequestCount == 2)
                {
                    Assert.Equal("https://api.github.com/user", request.RequestUri!.ToString());
                    response = """{"id":123,"login":"githubuser"}""";
                }
                else
                {
                    Assert.Equal("https://api.github.com/user/emails", request.RequestUri!.ToString());
                    response = """[{"email":"user@example.com","verified":true,"primary":true}]""";
                }
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
