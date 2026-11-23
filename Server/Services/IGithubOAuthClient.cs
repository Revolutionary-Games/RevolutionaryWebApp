namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

public interface IGithubOAuthClient
{
    public Task<(GithubUserInfo User, List<GithubEmail> Emails)> GetUser(string code, string redirectUri);
}

public class GithubOAuthClient : IGithubOAuthClient
{
    public const string RequiredScopes = "read:user user:email";
    public const string CallbackPath = "OAuthReturn/github";

    private readonly IHttpClientFactory clientFactory;
    private readonly IConfiguration configuration;

    public GithubOAuthClient(IHttpClientFactory clientFactory, IConfiguration configuration)
    {
        this.clientFactory = clientFactory;
        this.configuration = configuration;
    }

    public async Task<(GithubUserInfo User, List<GithubEmail> Emails)> GetUser(string code, string redirectUri)
    {
        using var client = clientFactory.CreateClient("github");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token");
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", configuration["Login:Github:ClientId"] ?? string.Empty },
            { "client_secret", configuration["Login:Github:ClientSecret"] ?? string.Empty },
            { "code", code },
            { "redirect_uri", redirectUri },
        });
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<GithubAccessTokenResponse>() ??
            throw new Exception("No access token returned from GitHub");

        Validator.ValidateObject(token, new ValidationContext(token), true);
        var scopes = token.Scope.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        if (RequiredScopes.Split(' ').Any(scope => !scopes.Contains(scope)))
            throw new Exception("GitHub did not grant the required scopes");

        if (!string.Equals(token.TokenType, "bearer", StringComparison.OrdinalIgnoreCase))
            throw new Exception("GitHub returned an unexpected token type");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        var user = await client.GetFromJsonAsync<GithubUserInfo>("https://api.github.com/user") ??
            throw new Exception("No user data returned from GitHub");

        if (string.IsNullOrWhiteSpace(user.Login) || user.Id == 0)
            throw new Exception("GitHub did not return a username or ID");

        var emails = await client.GetFromJsonAsync<List<GithubEmail>>("https://api.github.com/user/emails") ??
            throw new Exception("No email data returned from GitHub");

        return (user, emails);
    }

    private sealed class GithubAccessTokenResponse
    {
        [Required]
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [Required]
        public string Scope { get; set; } = string.Empty;

        [Required]
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
    }
}
