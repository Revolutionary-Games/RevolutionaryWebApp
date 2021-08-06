using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Models;
    using Utilities;

    /// <summary>
    ///   Handles github OAuth authorization
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     We re-use the SSO logic here as this kind of does logins
    ///   </para>
    /// </remarks>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class OAuthController : SSOLoginController
    {
        private const string GithubCLASource = "githubCLA";
        private const string WantedGithubCLAScopes = "read:user";

        private readonly Uri baseUrl;
        private readonly string githubClientId;
        private readonly string githubClientSecret;
        private readonly ILogger<GithubAPI> githubLog;

        private readonly bool githubConfigured;

        public OAuthController(ILogger<OAuthController> logger, IConfiguration configuration,
            NotificationsEnabledDb database, ILogger<GithubAPI> githubLog) : base(logger, database)
        {
            this.githubLog = githubLog;
            baseUrl = configuration.GetBaseUrl();

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

        [HttpPost("start/github/cla")]
        public async Task<ActionResult<JSONWrappedRedirect>> GithubStartForCLA()
        {
            if (!githubConfigured)
                return Problem("Github OAuth is not configured on the server");

            var session = await HttpContext.Request.Cookies.GetSession(Database);

            if (session == null)
                return this.WorkingForbid("You don't have an active session");

            var signature = await Database.InProgressClaSignatures.AsQueryable()
                .FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (signature == null)
                return NotFound("No active signature to authorize against");

            var returnTo = new Uri(baseUrl, "api/v1/OAuth/githubReturn").ToString();
            SetupSessionForSSO(GithubCLASource, returnTo, session);

            await Database.SaveChangesAsync();
            Logger.LogInformation("OAuth started for Github for attaching to a signature, session {Id}", session.Id);

            return new JSONWrappedRedirect()
            {
                RedirectTo = QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize",
                    new Dictionary<string, string>()
                    {
                        { "client_id", githubClientId },
                        { "redirect_uri", returnTo },
                        { "scope", WantedGithubCLAScopes },
                        { "state", session.SsoNonce },
                    }),
            };
        }

        [HttpGet("githubReturn")]
        public async Task<IActionResult> GithubReturn([Required] string code, [Required] string state)
        {
            if (!githubConfigured)
                return Problem("Github OAuth is not configured on the server");

            var (session, result) = await FetchAndCheckSessionForSsoReturn(state, GithubCLASource);

            // Return in case of failure
            if (result != null)
                return result;

            var signature = await Database.InProgressClaSignatures.AsQueryable()
                .FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (signature == null)
                return NotFound("No active signature to authorize against");

            var accessToken = await GetAccessTokenFromCode(code, session.SsoReturnUrl);

            if (string.IsNullOrEmpty(accessToken))
                return this.WorkingForbid("Failed to retrieve access token from Github");

            // We got an access token, authentication was successful. Now we can fetch the user data
            var githubAPI = new GithubAPI(githubLog, accessToken) { ThrowIfNotConfigured = true };

            GithubUserInfo user;

            try
            {
                user = await githubAPI.GetCurrentUserInfo();

                if (string.IsNullOrWhiteSpace(user.Login) || user.Id == 0)
                    throw new Exception("Didn't get username or id");
            }
            catch (Exception e)
            {
                Logger.LogWarning("Failed to get user info from github due to error: {@E}", e);
                return this.WorkingForbid("Failed to retrieve user info from Github");
            }

            ClearSSOParametersFromSession(session);

            // We store both username and user id to be able to detect if someone changes their username (as an extra
            // guarantee)
            signature.GithubAccount = user.Login;
            signature.GithubUserId = user.Id;
            signature.GithubEmail = user.Email;
            signature.GithubSkipped = false;

            await Database.SaveChangesAsync();
            Logger.LogInformation(
                "OAuth through Github succeeded, attaching {Login} ({Id1}) to signature of session {Id2}" +
                " (github email: {Email})",
                user.Login, user.Id, session.Id, user.Email);

            return Redirect("/cla/sign");
        }

        [NonAction]
        private async Task<string> GetAccessTokenFromCode(string code, string sessionRedirectUri)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            try
            {
                var response = await client.PostAsync(QueryHelpers.AddQueryString(
                    "https://github.com/login/oauth/access_token",
                    new Dictionary<string, string>()
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
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (data == null)
                    throw new Exception("Github response resulted in null json data deserialization");

                if (data.Scope != WantedGithubCLAScopes)
                {
                    throw new Exception(
                        $"We didn't get the scopes we asked for {data.Scope} != {WantedGithubCLAScopes}");
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
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; }

            public string Scope { get; set; }

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; }
        }
    }
}
