namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication;
using DevCenterCommunication.Models;
using DevCenterCommunication.Utilities;
using Filters;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Models;
using Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using SharedBase.Utilities;
using Utilities;

[ApiController]
[Route("LoginController")]
public class LoginController : SSOLoginController
{
    public const string SsoTypeDevForum = "devforum";
    public const string SsoTypeCommunityForum = "communityforum";
    public const string SsoTypePatreon = "patreon";

    private const string DiscourseSsoEndpoint = "/session/sso_provider";

    private readonly IConfiguration configuration;
    private readonly ITokenVerifier csrfVerifier;
    private readonly RedirectVerifier redirectVerifier;
    private readonly IPatreonAPI patreonAPI;
    private readonly IBackgroundJobClient jobClient;

    private readonly bool useSecureCookies;
    private readonly bool localLoginEnabled;

    public LoginController(ILogger<LoginController> logger, NotificationsEnabledDb database,
        IConfiguration configuration, ITokenVerifier csrfVerifier,
        RedirectVerifier redirectVerifier, IPatreonAPI patreonAPI, IBackgroundJobClient jobClient) : base(logger,
        database)
    {
        this.configuration = configuration;
        this.csrfVerifier = csrfVerifier;
        this.redirectVerifier = redirectVerifier;
        this.patreonAPI = patreonAPI;
        this.jobClient = jobClient;

        useSecureCookies = Convert.ToBoolean(configuration["Login:SecureCookies"]);

        localLoginEnabled = Convert.ToBoolean(configuration["Login:Local:Enabled"]);
    }

    private bool DevForumConfigured => !string.IsNullOrEmpty(configuration["Login:DevForum:SsoSecret"]);
    private bool CommunityForumConfigured => !string.IsNullOrEmpty(configuration["Login:CommunityForum:SsoSecret"]);

    private bool PatreonConfigured => !string.IsNullOrEmpty(configuration["Login:Patreon:ClientId"]) &&
        !string.IsNullOrEmpty(configuration["Login:Patreon:ClientSecret"]);

    /// <summary>
    ///   Sets the session cookie for a session in a response
    /// </summary>
    /// <param name="session">Session to give the cookie for</param>
    /// <param name="response">Response to put cookie in</param>
    /// <param name="useSecureCookies">If true the cookie is marked to only be passed with HTTPS</param>
    [NonAction]
    public static void SetSessionCookie(Session session, HttpResponse response, bool useSecureCookies = true)
    {
        var options = new CookieOptions
        {
            Expires = DateTime.UtcNow.AddSeconds(AppInfo.ClientCookieExpirySeconds),
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,

            // TODO: do we need to set the domain explicitly?
            // options.Domain;

            Secure = useSecureCookies,

            // Sessions are used for logins, they are essential. This might need to be re-thought out if
            // non-essential info is attached to sessions later
            IsEssential = true,
        };

        // -1 used as the placeholder when user not set
        long userId = -1;

        if (session.User != null)
            userId = session.User.Id;

        response.Cookies.Append(AppInfo.SessionCookieName, $"{session.Id}:{userId}", options);
    }

    [HttpGet]
    public LoginOptions Get()
    {
        return new LoginOptions
        {
            Categories = new List<LoginCategory>
            {
                new()
                {
                    Name = "Developer login",
                    Options = new List<LoginOption>
                    {
                        new()
                        {
                            ReadableName = "Login Using a Development Forum Account",
                            InternalName = SsoTypeDevForum,
                            Active = DevForumConfigured,
                        },
                    },
                },
                new()
                {
                    Name = "Supporter (patron) login",
                    Options = new List<LoginOption>
                    {
                        new()
                        {
                            ReadableName = "Login Using a Community Forum Account",
                            InternalName = SsoTypeCommunityForum,
                            Active = CommunityForumConfigured,
                        },
                        new()
                        {
                            ReadableName = "Login Using Patreon",
                            InternalName = SsoTypePatreon,
                            Active = PatreonConfigured,
                        },
                    },
                },
                new()
                {
                    Name = "Local Account",
                    Options = new List<LoginOption>
                    {
                        new()
                        {
                            ReadableName = "Login using a local account",
                            InternalName = "local",
                            Active = localLoginEnabled,
                            Local = true,
                        },
                    },
                },
            },
        };
    }

    [HttpPost("start")]
    public async Task<IActionResult> StartSsoLogin([FromForm] [Required] SsoStartFormData data)
    {
        await PerformPreLoginChecks(data.CSRF);

        switch (data.SsoType)
        {
            case SsoTypeDevForum:
            {
                if (!DevForumConfigured)
                    return CreateResponseForDisabledOption();

                return await DoDiscourseLoginRedirect(SsoTypeDevForum,
                    configuration["Login:DevForum:SsoSecret"] ??
                    throw new InvalidOperationException("Missing dev forum sso secret config value"),
                    configuration["Login:DevForum:BaseUrl"] ??
                    throw new InvalidOperationException("Missing dev forum base url config value"), data.ReturnUrl);
            }

            case SsoTypeCommunityForum:
            {
                if (!CommunityForumConfigured)
                    return CreateResponseForDisabledOption();

                return await DoDiscourseLoginRedirect(SsoTypeCommunityForum,
                    configuration["Login:CommunityForum:SsoSecret"] ??
                    throw new InvalidOperationException("Missing community sso secret config value"),
                    configuration["Login:CommunityForum:BaseUrl"] ??
                    throw new InvalidOperationException("Missing community base url config value"), data.ReturnUrl);
            }

            case SsoTypePatreon:
            {
                if (!PatreonConfigured)
                    return CreateResponseForDisabledOption();

                var returnUrl = new Uri(configuration.GetBaseUrl(), $"/LoginController/return/{SsoTypePatreon}")
                    .ToString();

                var session = await BeginSsoLogin(data.SsoType, data.ReturnUrl);

                if (string.IsNullOrEmpty(session.SsoNonce))
                    throw new Exception("sso begin failed to set nonce");

                var scopes = "identity identity[email]";

                return Redirect(QueryHelpers.AddQueryString(configuration["Login:Patreon:BaseUrl"] ??
                    throw new InvalidOperationException("Missing Patreon base url config value"),
                    new Dictionary<string, string?>
                    {
                        { "response_type", "code" },
                        { "client_id", configuration["Login:Patreon:ClientId"] },
                        { "redirect_uri", returnUrl },
                        { "scope", scopes },
                        { "state", session.SsoNonce },
                    }));
            }
        }

        return Redirect(QueryHelpers.AddQueryString("/login", "error", "Invalid SsoType"));
    }

    [HttpGet("return/" + SsoTypeDevForum)]
    public async Task<IActionResult> SsoReturnDev([Required] string sso, [Required] string sig)
    {
        if (!DevForumConfigured)
            return CreateResponseForDisabledOption();

        return await HandleDiscourseSsoReturn(sso, sig, SsoTypeDevForum);
    }

    [HttpGet("return/" + SsoTypeCommunityForum)]
    public async Task<IActionResult> SsoReturnCommunity([Required] string sso, [Required] string sig)
    {
        if (!CommunityForumConfigured)
            return CreateResponseForDisabledOption();

        return await HandleDiscourseSsoReturn(sso, sig, SsoTypeCommunityForum);
    }

    [HttpGet("return/" + SsoTypePatreon)]
    public async Task<IActionResult> SsoReturnPatreon([Required] string state, string? code, string? error)
    {
        if (!PatreonConfigured)
            return CreateResponseForDisabledOption();

        if (!string.IsNullOrEmpty(error))
        {
            // TODO: is it safe to show this to the user?
            return Redirect(QueryHelpers.AddQueryString("/login", "error", $"Error from patreon: {error}"));
        }

        return await HandlePatreonSsoReturn(state, code);
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitCategories.LoginLimit)]
    public async Task<IActionResult> PerformLocalLogin([FromForm] LoginFormData login)
    {
        if (!localLoginEnabled)
            return CreateResponseForDisabledOption();

        await PerformPreLoginChecks(login.CSRF);

        var user = await Database.Users.FirstOrDefaultAsync(u => u.Email == login.Email && u.Local);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash) ||
            !Passwords.CheckPassword(user.PasswordHash, login.Password))
        {
            return Redirect(QueryHelpers.AddQueryString("/login", "error", "Invalid username or password"));
        }

        // Suspended user cannot login
        if (user.Suspended)
        {
            return CreateSuspendedUserRedirect(user);
        }

        await user.ComputeUserGroups(Database);

        // Login is successful
        await BeginNewSession(user);

        if (string.IsNullOrEmpty(login.ReturnUrl) ||
            !redirectVerifier.SanitizeRedirectUrl(login.ReturnUrl, out var redirect))
        {
            return Redirect("/");
        }

        return Redirect(redirect ?? "/");
    }

    [NonAction]
    private async Task BeginNewSession(User user)
    {
        if (user.Suspended)
            throw new InvalidOperationException("Cannot begin a session for a suspended user");

        var remoteAddress = HttpContext.Connection.RemoteIpAddress;

        // Re-use existing session if there is one
        var session = await HttpContext.Request.Cookies.GetSession(Database);

        if (session == null)
        {
            session = new Session();

            await Database.Sessions.AddAsync(session);
        }
        else
        {
            session = await CreateDerivedSessionForLogin(session);
        }

        session.LastLoggedIn = DateTime.UtcNow;

        session.User = user;
        session.UserId = user.Id;
        session.LastUsedFrom = remoteAddress;
        session.LastUsed = DateTime.UtcNow;
        session.CachedUserGroups = user.AccessCachedGroupsOrThrow();

        await Database.SaveChangesAsync();

        Logger.LogInformation("Successful login for user {Email} from {RemoteAddress}, session: {Id}", user.Email,
            remoteAddress, session.Id);

        SetSessionCookie(session, Response, useSecureCookies);
    }

    [NonAction]
    private async Task<Session> CreateDerivedSessionForLogin(Session session)
    {
        var oldSession = session;

        // Create a duplicate of the session with a different ID to make the negative session user cache not mess
        // with the freshly logged in user
        session = session.CreateSessionCopy();
        await Database.Sessions.AddAsync(session);

        await UpdateSessionDependentResources(oldSession.Id, session.Id);

        // Delete the old session to make sure it is no longer valid
        Database.Sessions.Remove(oldSession);

        // Just for extra safety clear the session user info here, just in case a mistake slips into the login
        // code
        session.User = null;
        session.UserId = null;
        session.CachedUserGroups = null;
        session.SsoNonce = null;

        // Clear the hashed id to force it to be recomputed
        session.HashedId = null;

        Logger.LogInformation("Repurposing session {OldId} for a new login, but it will be renamed to {Id}",
            oldSession.Id, session.Id);

        return session;
    }

    /// <summary>
    ///   Updates all <see cref="Session"/> dependent resources to point to a new session
    /// </summary>
    /// <param name="oldSessionId">The old ID to modify from</param>
    /// <param name="sessionId">The new ID to put in the dependent resources</param>
    [NonAction]
    private async Task UpdateSessionDependentResources(Guid oldSessionId, Guid sessionId)
    {
        var inProgressSignatures =
            await Database.InProgressClaSignatures.Where(s => s.SessionId == oldSessionId).ToListAsync();

        foreach (var inProgressCla in inProgressSignatures)
        {
            Logger.LogInformation(
                "Re-attaching in-progress CLA signature {Id} from session {OldSessionId} to {SessionId}",
                inProgressCla.Id, oldSessionId, sessionId);
            inProgressCla.SessionId = sessionId;
        }
    }

    [NonAction]
    private IActionResult CreateResponseForDisabledOption()
    {
        return Redirect(QueryHelpers.AddQueryString("/login", "error", "This login option is not enabled"));
    }

    [NonAction]
    private IActionResult CreateSuspendedUserRedirect(User user)
    {
        string suspension;
        if (string.IsNullOrWhiteSpace(user.SuspendedReason))
        {
            if (user.SuspendedManually)
            {
                suspension = " manually by an admin";
            }
            else
            {
                suspension = " due to an unknown reason";
            }
        }
        else
        {
            if (user.SuspendedManually)
            {
                suspension = $" manually by an admin with the reason: {user.SuspendedReason}";
            }
            else
            {
                suspension = $" with the reason: {user.SuspendedReason}";
            }
        }

        return Redirect(QueryHelpers.AddQueryString("/login", "error", $"Your account is suspended {suspension}"));
    }

    [NonAction]
    private async Task PerformPreLoginChecks(string csrf)
    {
        var existingSession = await HttpContext.Request.Cookies.GetSession(Database);

        // TODO: could maybe relax the CSRF requirement for SSO login?
        if (!csrfVerifier.IsValidCSRFToken(csrf, existingSession?.User))
        {
            throw new HttpResponseException
            {
                Value = "Invalid CSRF token. Please go to the previous page, refresh it fully " +
                    "and then try logging in again.",
            };
        }

        if (existingSession != null)
        {
            // If there is an existing session, end it if it is close to expiring
            if (existingSession.IsCloseToExpiry())
            {
                Logger.LogInformation(
                    "Destroying an existing session for starting login as it is close to expiring {Id}",
                    existingSession.Id);
                await LogoutController.PerformSessionDestroy(existingSession, Database);
            }
            else
            {
                // This used to clear the logged in data, but for SSO start we don't really want to do that yet (only
                // on return / success)
                existingSession.LastUsed = DateTime.UtcNow;

                Logger.LogInformation("Login starting for an existing session {Id}", existingSession.Id);
            }
        }
    }

    [NonAction]
    private async Task<Session> BeginSsoLogin(string ssoSource, string? returnTo)
    {
        // Re-use existing session if there is one
        var session = await HttpContext.Request.Cookies.GetSession(Database);

        if (session == null)
        {
            session = new Session();
            await Database.Sessions.AddAsync(session);
        }
        else
        {
            Logger.LogInformation("SSO login will use an existing session ({Id})", session.Id);

            // As SSO start might not be protected by a CSRF, don't delete the login session here yet
        }

        SetupSessionForSSO(ssoSource, returnTo, session);

        await Database.SaveChangesAsync();

        SetSessionCookie(session, Response, useSecureCookies);

        return session;
    }

    [NonAction]
    private async Task<IActionResult> DoDiscourseLoginRedirect(string ssoType, string secret, string redirectBase,
        string? returnUrlOnSuccess)
    {
        var returnUrl = new Uri(configuration.GetBaseUrl(), $"/LoginController/return/{ssoType}").ToString();

        var session = await BeginSsoLogin(ssoType, returnUrlOnSuccess);

        if (string.IsNullOrEmpty(session.SsoNonce))
            throw new Exception("Can't create login redirect without existing session nonce");

        var payload = PrepareDiscoursePayload(session.SsoNonce, returnUrl);

        var signature = CalculateDiscourseSsoParamSignature(payload, secret);

        return Redirect(QueryHelpers.AddQueryString(new Uri(new Uri(redirectBase), DiscourseSsoEndpoint).ToString(),
            new Dictionary<string, string?>
            {
                { "sso", payload },
                { "sig", signature },
            }));
    }

    [NonAction]
    private string CalculateDiscourseSsoParamSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));

        // We need the hex string in lowercase, so we need to convert it here as there doesn't seem to be a built in
        // way to do that
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    [NonAction]
    private string PrepareDiscoursePayload(string nonce, string returnUrl)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"nonce={nonce}&return_sso_url={returnUrl}"));
    }

    [NonAction]
    private async Task<IActionResult> HandleDiscourseSsoReturn(string ssoPayload, string signature, string ssoType)
    {
        string secret;
        bool developer;

        switch (ssoType)
        {
            case SsoTypeDevForum:
                secret = configuration["Login:DevForum:SsoSecret"] ??
                    throw new InvalidOperationException("Missing dev forum sso secret config value");
                developer = true;
                break;
            case SsoTypeCommunityForum:
                secret = configuration["Login:CommunityForum:SsoSecret"] ??
                    throw new InvalidOperationException("Missing community sso secret config value");
                developer = false;
                break;
            default:
                throw new ArgumentException("invalid discourse ssoType");
        }

        // Make sure the signature is right first
        var actualRequestSignature = CalculateDiscourseSsoParamSignature(ssoPayload, secret);

        if (actualRequestSignature != signature)
            return GetInvalidSsoParametersResult();

        // TODO: exception catching here?
        var payload = QueryHelpers.ParseQuery(Encoding.UTF8.GetString(Convert.FromBase64String(ssoPayload)));

        if (!payload.TryGetValue("nonce", out StringValues payloadNonce) || payloadNonce.Count != 1)
            return GetInvalidSsoParametersResult();

        var (session, result) = await FetchAndCheckSessionForSsoReturn(payloadNonce[0], ssoType);

        // Return in case of failure
        if (result != null)
            return result;

        if (session == null)
            throw new Exception("Logic error, returned null session without returning an error result");

        bool requireSave = true;

        try
        {
            if (!payload.TryGetValue("email", out StringValues emailRaw) || emailRaw.Count != 1)
                return GetInvalidSsoParametersResult();

            var email = emailRaw[0];

            if (ssoType == SsoTypeCommunityForum)
            {
                // Check membership in required groups

                if (!payload.TryGetValue("groups", out StringValues groups))
                    return GetInvalidSsoParametersResult();

                var parsedGroups = groups.Where(g => g != null).SelectMany(g => g!.Split(','));

                if (!parsedGroups.Any(group =>
                        DiscourseApiHelpers.CommunityDevBuildGroup.Equals(group) ||
                        DiscourseApiHelpers.CommunityVIPGroup.Equals(group)))
                {
                    Logger.LogInformation(
                        "Not allowing login due to missing group membership for: {Email}, groups: {ParsedGroups}",
                        email,
                        parsedGroups);
                    return Redirect(QueryHelpers.AddQueryString("/login", "error",
                        "You must be either in the Supporter or VIP supporter group to login. " +
                        "These are granted to our Patrons. If you just signed up, please wait up to an " +
                        "hour for groups to sync."));
                }
            }

            var username = email;

            if (payload.TryGetValue("username", out StringValues usernameRaw) && usernameRaw.Count > 0)
            {
                username = usernameRaw[0];
            }

            var tuple = await HandleSsoLoginToAccount(session, email, username, ssoType, developer);
            requireSave = !tuple.Saved;
            return tuple.Result;
        }
        finally
        {
            if (requireSave)
                await Database.SaveChangesAsync();
        }
    }

    [NonAction]
    private async Task<IActionResult> HandlePatreonSsoReturn(string state, string? code)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return GetInvalidSsoParametersResult();

        var (session, result) = await FetchAndCheckSessionForSsoReturn(state, SsoTypePatreon);

        // Return in case of failure
        if (result != null)
            return result;

        if (session == null)
            throw new Exception("Logic error, returned null session without returning an error result");

        bool requireSave = true;

        try
        {
            patreonAPI.Initialize(configuration["Login:Patreon:ClientId"] ??
                throw new InvalidOperationException("Missing PatreonAPI client id"),
                configuration["Login:Patreon:ClientSecret"] ??
                throw new InvalidOperationException("Missing PatreonAPI client secret"));

            // We need to fetch the actual user email and details directly from Patreon's API
            var token = await patreonAPI.TurnCodeIntoTokens(code,
                new Uri(configuration.GetBaseUrl(), $"/LoginController/return/{SsoTypePatreon}")
                    .ToString());

            patreonAPI.LoginAsUser(token);

            var userDetails = await patreonAPI.GetOwnDetails();

            var email = userDetails.Data.Attributes.Email;

            if (email == null)
                throw new NullReferenceException();

            var patron = await Database.Patrons.Where(p => p.Email == email).FirstOrDefaultAsync();

            if (patron == null)
            {
                return Redirect(QueryHelpers.AddQueryString("/login", "error",
                    "You aren't a patron of Thrive Game according to our latest information. " +
                    "Please become our patron and try again."));
            }

            if (patron.Suspended == true)
            {
                return Redirect(QueryHelpers.AddQueryString("/login", "error",
                    $"Your Patron status is currently suspended. Reason: {patron.SuspendedReason}"));
            }

            var patreonSettings = await Database.PatreonSettings.OrderBy(s => s.Id).FirstOrDefaultAsync();

            if (patreonSettings == null)
            {
                return Redirect(QueryHelpers.AddQueryString("/login", "error",
                    "Patreon settings are currently unconfigured, please contact a site admin."));
            }

            if (!patreonSettings.IsEntitledToDevBuilds(patron))
            {
                return Redirect(QueryHelpers.AddQueryString("/login", "error",
                    "Your current reward is not the DevBuilds or higher tier"));
            }

            Logger.LogInformation("Patron ({Email}) logging in", email);

            // TODO: alias handling
            // email = patron.EmailAlias ?? patron.Email;

            var tuple = await HandleSsoLoginToAccount(session, email, patron.Username, SsoTypePatreon, false);
            requireSave = !tuple.Saved;
            return tuple.Result;
        }
        catch (Exception e)
        {
            Logger.LogWarning("Exception when processing Patreon return: {@E}", e);
            return Redirect(QueryHelpers.AddQueryString("/login", "error",
                "Failed to retrieve account details from Patreon."));
        }
        finally
        {
            if (requireSave)
                await Database.SaveChangesAsync();
        }
    }

    [NonAction]
    private async Task<(IActionResult Result, bool Saved)> HandleSsoLoginToAccount(Session session, string? email,
        string? username, string ssoType, bool developerLogin)
    {
        // Ensure whitespace is consistent
        email = email?.Trim();

        var displayName = username?.Trim();
        username = Normalization.NormalizeUserName(username ?? string.Empty);

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(username))
            return (GetInvalidSsoParametersResult(), false);

        Logger.LogInformation("Logging in SSO login user with email: {Email}", email);

        var user = await Database.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user != null)
            await user.ComputeUserGroups(Database);

        var developerGroup = new Lazy<Task<UserGroup>>(async () =>
            await Database.UserGroups.FindAsync(GroupType.Developer) ??
            throw new Exception("Developer group not found"));

        if (user == null)
        {
            // New account needed
            Logger.LogInformation("Creating new account for SSO login: {Email} developer: {DeveloperLogin}",
                email, developerLogin);

            var newUser = new User(email, username)
            {
                Local = false,
                SsoSource = ssoType,
                DisplayName = displayName,
            };

            newUser.ComputeNormalizedEmail();

            if (developerLogin)
            {
                newUser.Groups.Add(await developerGroup.Value);
                newUser.OnGroupsChanged(jobClient, true);
            }

            await Database.Users.AddAsync(newUser);
            Models.User.OnNewUserCreated(newUser, jobClient);
            user = newUser;
        }
        else if (user.Local)
        {
            // TODO: allow (non-developers) to login using different sso
            // Maybe developers with 2fa should be allowed?
            return (Redirect(QueryHelpers.AddQueryString("/login", "error",
                "Can't login to local account using SSO. If you can't login with local login, please reset " +
                "your password with your email and try it again.")), false);
        }
        else if (user.SsoSource != ssoType)
        {
            Logger.LogInformation(
                "User logged in with different SSO source than before, new: {SsoType}, old: {SsoSource}", ssoType,
                user.SsoSource);

            if (user.SsoSource == SsoTypeDevForum ||
                user.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Developer))
            {
                return (Redirect(QueryHelpers.AddQueryString("/login", "error",
                        "Your account is a developer account. You need to login through the Development Forums.")),
                    false);
            }

            // TODO: remove most of these account type changes
            if (ssoType == SsoTypeDevForum)
            {
                // Conversion to a developer account
                await Database.LogEntries.AddAsync(
                    new LogEntry("User is now a developer due to different SSO login type")
                    {
                        TargetUser = user,
                    });

                // Groups are loaded with ComputeUserGroups
                // ReSharper disable once EntityFramework.NPlusOne.IncompleteDataUsage
                user.Groups.Add(await developerGroup.Value);
                user.OnGroupsChanged(jobClient);
                user.SsoSource = SsoTypeDevForum;
                user.BumpUpdatedAt();
            }
            else if (user.SsoSource == SsoTypePatreon && ssoType == SsoTypeCommunityForum)
            {
                Logger.LogInformation("Patron logged in using a community forum account");
                ChangeSsoSourceForNormalUser(user, ssoType);
            }
            else if (user.SsoSource == SsoTypeCommunityForum && ssoType == SsoTypePatreon)
            {
                Logger.LogInformation("Community forum user logged in using patreon");
                ChangeSsoSourceForNormalUser(user, ssoType);
            }
            else
            {
                throw new Exception("Unknown sso type (old, new) combination to move an user to");
            }
        }

        if (user.Suspended)
        {
            if (!await CheckCanAutoUnsuspend(user))
            {
                Logger.LogInformation("Suspended user tried to login, SSO auto unsuspend not possible");
                return (CreateSuspendedUserRedirect(user), false);
            }
        }

        // Change username from SSO login
        if (user.UserName != username)
        {
            Logger.LogInformation("Trying to change username for {Email} from {Username} to {Username2} " +
                "due to SSO login with new name", email, user.UserName, username);

            var conflictingUser = await Database.Users.FirstOrDefaultAsync(u => u.UserName == username);

            if (conflictingUser != null)
            {
                Logger.LogError("Can't change SSO logged in user's username due to a conflict, leaving as-is");
            }
            else
            {
                await Database.LogEntries.AddAsync(
                    new LogEntry($"Username changed due to SSO login to \"{username}\"", "Previous: " + user.UserName)
                    {
                        TargetUserId = user.Id,
                    });

                user.UserName = username;

                // Save should happen in FinishSsoLoginToAccount
            }
        }

        var result = await FinishSsoLoginToAccount(user, session);
        return (result, true);
    }

    [NonAction]
    private async Task<bool> CheckCanAutoUnsuspend(User user)
    {
        // Manually suspended users cannot auto-unsuspend
        if (user.SuspendedManually)
            return false;

        if (user.SuspendedReason != null &&
            user.SuspendedReason.Contains(SSOSuspendHandler.LoginOptionNoLongerValidText))
        {
            Logger.LogInformation("Auto un-suspending user as SSO login is about to succeed for {Email}",
                user.Email);

            user.Suspended = false;

            await Database.LogEntries.AddAsync(new LogEntry("User unsuspended automatically due to SSO login success")
            {
                TargetUserId = user.Id,
            });

            // We rely on save happening later
            return true;
        }

        return false;
    }

    [NonAction]
    private void ChangeSsoSourceForNormalUser(User user, string ssoType)
    {
        if (user.SuspendedManually || user.SsoSource == ssoType)
            return;

        user.SsoSource = ssoType;
    }

    [NonAction]
    private async Task<IActionResult> FinishSsoLoginToAccount(User user, Session session)
    {
        var remoteAddress = HttpContext.Connection.RemoteIpAddress;

        string? returnUrl = session.SsoReturnUrl;

        session = await CreateDerivedSessionForLogin(session);

        session.User = user;
        session.UserId = user.Id;
        session.LastUsed = DateTime.UtcNow;
        session.CachedUserGroups = user.AccessCachedGroupsOrThrow();

        ClearSSOParametersFromSession(session);

        await Database.SaveChangesAsync();

        // Need to print here to get user id for new users working (after the DB save)
        Logger.LogInformation("SSO login succeeded to user id: {Id}, from: {RemoteAddress}, session: {SessionId}",
            user.Id, remoteAddress, session.Id);

        // Send updated cookie in the response
        SetSessionCookie(session, Response, useSecureCookies);

        if (string.IsNullOrEmpty(returnUrl) ||
            !redirectVerifier.SanitizeRedirectUrl(returnUrl, out string? redirect))
        {
            return Redirect("/");
        }

        return Redirect(redirect ?? "/");
    }
}

public class LoginFormData
{
    [Required]
    [MaxLength(GlobalConstants.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(AppInfo.MaxPasswordLength)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [MaxLength(CommunicationConstants.MAXIMUM_TOKEN_LENGTH)]
    public string CSRF { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public class SsoStartFormData
{
    [Required]
    [MaxLength(300)]
    public string SsoType { get; set; } = string.Empty;

    [Required]
    [MaxLength(CommunicationConstants.MAXIMUM_TOKEN_LENGTH)]
    public string CSRF { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}
