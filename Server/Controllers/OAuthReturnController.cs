namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Utilities;

[ApiController]
[Route("[controller]")]
public class OAuthReturnController : SSOLoginController
{
    private readonly IGithubOAuthClient githubOAuth;
    private readonly ICLAExemptions claExemptions;

    private readonly bool githubConfigured;

    // ReSharper disable ContextualLoggerProblem
    public OAuthReturnController(ILogger<OAuthReturnController> logger, IConfiguration configuration,
        NotificationsEnabledDb database, IGithubOAuthClient githubOAuth, ICLAExemptions claExemptions) : base(logger,
        database)
    {
        // ReSharper restore ContextualLoggerProblem

        this.githubOAuth = githubOAuth;
        this.claExemptions = claExemptions;

        // These are local variables as the client interface does authentication, so we just know whether this is
        // configured or not
        var githubClientId1 = configuration["Login:Github:ClientId"] ?? string.Empty;
        var githubClientSecret1 = configuration["Login:Github:ClientSecret"] ?? string.Empty;

        if (string.IsNullOrEmpty(githubClientId1) || string.IsNullOrEmpty(githubClientSecret1))
        {
            githubConfigured = false;
        }
        else
        {
            githubConfigured = true;
        }
    }

    [HttpGet("github")]
    public async Task<IActionResult> GithubReturn([Required] string state, string? code, string? error)
    {
        if (!githubConfigured)
            return Problem("Github OAuth is not configured on the server");

        var currentSession = await HttpContext.Request.Cookies.GetSession(Database);
        if (currentSession?.StartedSsoLogin == LoginController.SsoTypeGithub)
        {
            // Keep using the registered GitHub callback for both login and CLA authorization.
            // The login controller validates and consumes the nonce before exchanging the code.
            return Redirect(QueryHelpers.AddQueryString("/LoginController/return/github",
                new Dictionary<string, string?>
                {
                    { "state", state },
                    { "code", code },
                    { "error", error },
                }));
        }

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

        var redirectUri = session.SsoReturnUrl;

        ClearSSOParametersFromSession(session);
        await Database.SaveChangesAsync();

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return this.WorkingForbid("GitHub authorization was cancelled or failed. Please try again.");

        GithubUserInfo user;
        List<GithubEmail> emails;
        try
        {
            (user, emails) = await githubOAuth.GetUser(code, redirectUri);
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Failed to get user info from github due to error");
            return this.WorkingForbid("Failed to retrieve user info from Github");
        }

        // Prefer to not get a bad GitHub email
        var email = emails.FirstOrDefault(e => e.Primary && e.Verified && !e.Email.Contains("users.noreply.github")) ??
            emails.FirstOrDefault(e => e.Verified);

        if (email == null)
        {
            return this.WorkingForbid(
                "Failed to get any verified email from your GitHub account (that is not a no-reply address)");
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
}
