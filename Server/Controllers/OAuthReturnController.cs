namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using SharedBase.Utilities;
using Utilities;

[ApiController]
[Route("[controller]")]
public class OAuthReturnController : SSOLoginController
{
    private readonly string githubClientId;
    private readonly string githubClientSecret;
    private readonly ILogger<GithubAPI> githubLog;
    private readonly ICLAExemptions claExemptions;

    private readonly bool githubConfigured;

    public OAuthReturnController(ILogger<OAuthReturnController> logger, IConfiguration configuration,

        // ReSharper disable once ContextualLoggerProblem
        NotificationsEnabledDb database, ILogger<GithubAPI> githubLog, ICLAExemptions claExemptions) : base(logger,
        database)
    {
        this.githubLog = githubLog;
        this.claExemptions = claExemptions;

        githubClientId = configuration["Login:Github:ClientId"];
        githubClientSecret = configuration["Login:Github:ClientSecret"];

        if (string.IsNullOrEmpty(githubClientId) || string.IsNullOrEmpty(githubClientSecret))
        {
            githubConfigured = false;
        }
        else
        {
            githubConfigured = true;
        }
    }

    [HttpGet("github")]
    public async Task<IActionResult> GithubReturn([Required] string code, [Required] string state)
    {
        if (!githubConfigured)
            return Problem("Github OAuth is not configured on the server");

        var (session, result) = await FetchAndCheckSessionForSsoReturn(state, OAuthController.GithubCLASource);

        // Return in case of failure
        if (result != null)
            return result;

        if (session == null)
            throw new Exception("Logic error, returned null session without returning an error result");

        var signature = await Database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);

        if (signature == null)
            return NotFound("No active signature to authorize against");

        if (string.IsNullOrEmpty(session.SsoReturnUrl))
            return NotFound("Session not setup with a return URL");

        var accessToken = await GetAccessTokenFromCode(code, session.SsoReturnUrl);

        // Clear things here already to make sure even on partial failure the request can't be repeated
        ClearSSOParametersFromSession(session);

        if (string.IsNullOrEmpty(accessToken))
            return this.WorkingForbid("Failed to retrieve access token from Github");

        // We got an access token, authentication was successful. Now we can fetch the user data
        var githubAPI = new GithubAPI(githubLog, accessToken) { ThrowIfNotConfigured = true };

        GithubUserInfo? user;

        List<GithubEmail> emails;
        try
        {
            user = await githubAPI.GetCurrentUserInfo() ?? throw new Exception("Github user data is null");

            if (string.IsNullOrWhiteSpace(user.Login) || user.Id == 0)
                throw new Exception("Didn't get username or id");

            emails = await githubAPI.GetCurrentUserEmails();

            if (emails == null || emails.Count < 1)
                throw new Exception("Didn't get user email info");
        }
        catch (Exception e)
        {
            Logger.LogWarning("Failed to get user info from github due to error: {@E}", e);
            return this.WorkingForbid("Failed to retrieve user info from Github");
        }

        var email = emails.FirstOrDefault(e => e.Primary && e.Verified) ?? emails.FirstOrDefault(e => e.Verified);

        if (email == null)
        {
            return this.WorkingForbid("Failed to get any verified email from your Github account");
        }

        // Disallow attaching if this is an account that doesn't need a CLA signature
        if (claExemptions.IsExempt(user.Login))
        {
            return this.WorkingForbid(
                "This Github account is exempt from CLA signing and can't be attached to a signature");
        }

        // We store both username and user id to be able to detect if someone changes their username (as an extra
        // guarantee)
        signature.GithubAccount = user.Login;
        signature.GithubUserId = user.Id;
        signature.GithubEmail = email.Email;
        signature.GithubSkipped = false;

        await Database.SaveChangesAsync();
        Logger.LogInformation(
            "OAuth through Github succeeded, attaching {Login} ({Id1}) to signature of session {Id2}" +
            " (github email: {GithubEmail})",
            user.Login, user.Id, session.Id, signature.GithubEmail);

        return Redirect("/cla/sign");
    }

    [NonAction]
    private async Task<string?> GetAccessTokenFromCode(string code, string sessionRedirectUri)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await client.PostAsync(QueryHelpers.AddQueryString(
                "https://github.com/login/oauth/access_token",
                new Dictionary<string, string?>
                {
                    { "client_id", githubClientId },
                    { "client_secret", githubClientSecret },
                    { "code", code },
                    { "redirect_uri", sessionRedirectUri },
                }), new StringContent(string.Empty));

            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"Non-success status code from Github: {response.StatusCode}, response: {content}");
            }

            var data = JsonSerializer.Deserialize<GithubAccessTokenResponse>(content,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new NullDecodedJsonException();

            Validator.ValidateObject(data, new ValidationContext(data));

            if (OAuthController.WantedGithubCLAScopes.Split(' ').Any(s => !data.Scope.Contains(s)))
            {
                throw new Exception(
                    $"We didn't get the scopes we asked for {data.Scope} != " +
                    $"{OAuthController.WantedGithubCLAScopes}");
            }

            if (data.TokenType != "bearer")
                throw new Exception($"Unexpected token type returned from github ({data.TokenType})");

            return data.AccessToken;
        }
        catch (Exception e)
        {
            Logger.LogWarning("Failed to get access token from github due to error: {@E}", e);
            return null;
        }
    }

    private class GithubAccessTokenResponse
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
