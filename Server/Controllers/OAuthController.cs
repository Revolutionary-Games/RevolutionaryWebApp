using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;
    using Utilities;

    /// <summary>
    ///   Handles github OAuth authorization (starting part OAuthReturnController handles returning)
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
        public const string GithubCLASource = "githubCLA";
        public const string WantedGithubCLAScopes = "read:user user:email";

        private readonly Uri baseUrl;
        private readonly string githubClientId;

        private readonly bool githubConfigured;

        public OAuthController(ILogger<OAuthController> logger, IConfiguration configuration,
            NotificationsEnabledDb database) : base(logger, database)
        {
            baseUrl = configuration.GetBaseUrl();

            githubClientId = configuration["Login:Github:ClientId"];

            githubConfigured = !string.IsNullOrEmpty(githubClientId);
        }

        [HttpPost("start/github/cla")]
        public async Task<ActionResult<JSONWrappedRedirect>> GithubStartForCLA()
        {
            if (!githubConfigured)
                return Problem("Github OAuth is not configured on the server");

            var session = await HttpContext.Request.Cookies.GetSession(Database);

            if (session == null)
                return this.WorkingForbid("You don't have an active session");

            var signature = await Database.InProgressClaSignatures.FirstOrDefaultAsync(s => s.SessionId == session.Id);

            if (signature == null)
                return NotFound("No active signature to authorize against");

            var returnTo = new Uri(baseUrl, "OAuthReturn/github").ToString();
            SetupSessionForSSO(GithubCLASource, returnTo, session);

            if (string.IsNullOrEmpty(session.SsoNonce))
                throw new Exception("Failed to setup sso nonce for OAuth in session");

            await Database.SaveChangesAsync();
            Logger.LogInformation("OAuth started for Github for attaching to a signature, session {Id}", session.Id);

            return new JSONWrappedRedirect()
            {
                RedirectTo = QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize",
                    new Dictionary<string, string?>()
                    {
                        { "client_id", githubClientId },
                        { "redirect_uri", returnTo },
                        { "scope", WantedGithubCLAScopes },
                        { "state", session.SsoNonce },
                    }),
            };
        }
    }
}
