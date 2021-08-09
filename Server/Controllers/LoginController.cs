using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading.Tasks;
    using Authorization;
    using Filters;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Services;
    using Shared;
    using Shared.Models;
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

        private readonly bool useSecureCookies;
        private readonly bool localLoginEnabled;

        public LoginController(ILogger<LoginController> logger, ApplicationDbContext database,
            IConfiguration configuration, ITokenVerifier csrfVerifier,
            RedirectVerifier redirectVerifier, IPatreonAPI patreonAPI) : base(logger, database)
        {
            this.configuration = configuration;
            this.csrfVerifier = csrfVerifier;
            this.redirectVerifier = redirectVerifier;
            this.patreonAPI = patreonAPI;

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
                IsEssential = true
            };

            response.Cookies.Append(AppInfo.SessionCookieName, session.Id.ToString(), options);
        }

        [HttpGet]
        public LoginOptions Get()
        {
            return new LoginOptions()
            {
                Categories = new List<LoginCategory>()
                {
                    new()
                    {
                        Name = "Developer login",
                        Options = new List<LoginOption>()
                        {
                            new()
                            {
                                ReadableName = "Login Using a Development Forum Account",
                                InternalName = SsoTypeDevForum,
                                Active = DevForumConfigured
                            }
                        }
                    },
                    new()
                    {
                        Name = "Supporter (patron) login",
                        Options = new List<LoginOption>()
                        {
                            new()
                            {
                                ReadableName = "Login Using a Community Forum Account",
                                InternalName = SsoTypeCommunityForum,
                                Active = CommunityForumConfigured
                            },
                            new()
                            {
                                ReadableName = "Login Using Patreon",
                                InternalName = SsoTypePatreon,
                                Active = PatreonConfigured
                            }
                        }
                    },
                    new()
                    {
                        Name = "Local Account",
                        Options = new List<LoginOption>()
                        {
                            new()
                            {
                                ReadableName = "Login using a local account",
                                InternalName = "local",
                                Active = localLoginEnabled,
                                Local = true
                            }
                        }
                    },
                }
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

                    return await DoDiscourseLoginRedirect(SsoTypeDevForum, configuration["Login:DevForum:SsoSecret"],
                        configuration["Login:DevForum:BaseUrl"], data.ReturnUrl);
                }
                case SsoTypeCommunityForum:
                {
                    if (!CommunityForumConfigured)
                        return CreateResponseForDisabledOption();

                    return await DoDiscourseLoginRedirect(SsoTypeCommunityForum,
                        configuration["Login:CommunityForum:SsoSecret"],
                        configuration["Login:CommunityForum:BaseUrl"], data.ReturnUrl);
                }
                case SsoTypePatreon:
                {
                    if (!PatreonConfigured)
                        return CreateResponseForDisabledOption();

                    var returnUrl = new Uri(configuration.GetBaseUrl(), $"/LoginController/return/{SsoTypePatreon}")
                        .ToString();

                    var session = await BeginSsoLogin(data.SsoType, data.ReturnUrl);

                    var scopes = "identity identity[email]";

                    return Redirect(QueryHelpers.AddQueryString(
                        configuration["Login:Patreon:BaseUrl"],
                        new Dictionary<string, string>()
                        {
                            { "response_type", "code" },
                            { "client_id", configuration["Login:Patreon:ClientId"] },
                            { "redirect_uri", returnUrl },
                            { "scope", scopes },
                            { "state", session.SsoNonce }
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
        public async Task<IActionResult> SsoReturnPatreon([Required] string state, string code, string error)
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
        public async Task<IActionResult> PerformLocalLogin([FromForm] LoginFormData login)
        {
            if (!localLoginEnabled)
                return CreateResponseForDisabledOption();

            await PerformPreLoginChecks(login.CSRF);

            var user = await Database.Users.AsQueryable().FirstOrDefaultAsync(u => u.Email == login.Email && u.Local);

            if (user == null || string.IsNullOrEmpty(user.PasswordHash) ||
                !Passwords.CheckPassword(user.PasswordHash, login.Password))
                return Redirect(QueryHelpers.AddQueryString("/login", "error", "Invalid username or password"));

            // Login is successful
            await BeginNewSession(user);

            if (string.IsNullOrEmpty(login.ReturnUrl) ||
                !redirectVerifier.SanitizeRedirectUrl(login.ReturnUrl, out string redirect))
            {
                return Redirect("/");
            }
            else
            {
                return Redirect(redirect);
            }
        }

        [NonAction]
        private async Task BeginNewSession(User user)
        {
            var remoteAddress = Request.HttpContext.Connection.RemoteIpAddress;

            // Re-use existing session if there is one
            var session = await HttpContext.Request.Cookies.GetSession(Database);

            if (session == null)
            {
                session = new Session();

                await Database.Sessions.AddAsync(session);
            }
            else
            {
                Logger.LogInformation("Repurposing session ({Id}) for new login", session.Id);

                session.SsoNonce = null;
                session.LastUsed = DateTime.UtcNow;
            }

            session.User = user;
            session.SessionVersion = user.SessionVersion;
            session.LastUsedFrom = remoteAddress;

            await Database.SaveChangesAsync();

            Logger.LogInformation("Successful login for user {Email} from {RemoteAddress}, session: {Id}", user.Email,
                remoteAddress, session.Id);

            SetSessionCookie(session, Response, useSecureCookies);
        }

        [NonAction]
        private IActionResult CreateResponseForDisabledOption()
        {
            return Redirect(QueryHelpers.AddQueryString("/login", "error", "This login option is not enabled"));
        }

        [NonAction]
        private async Task PerformPreLoginChecks(string csrf)
        {
            var existingSession = await HttpContext.Request.Cookies.GetSession(Database);

            // TODO: verify that the client making the request had up to date token
            if (!csrfVerifier.IsValidCSRFToken(csrf, existingSession?.User))
            {
                throw new HttpResponseException()
                    { Value = "Invalid CSRF token. Please refresh and try logging in again" };
            }

            if (existingSession != null)
            {
                // If there is an existing session, end it (if it is close to expiring)
                if (existingSession.IsCloseToExpiry())
                {
                    Logger.LogInformation(
                        "Destroying an existing session for starting login as it is close to expiring {Id}",
                        existingSession.Id);
                    await LogoutController.PerformSessionDestroy(existingSession, Database);
                }
                else
                {
                    existingSession.User = null;
                    existingSession.LastUsed = DateTime.UtcNow;
                    Logger.LogInformation("Login starting for an existing session {Id}", existingSession.Id);
                }
            }
        }

        [NonAction]
        private async Task<Session> BeginSsoLogin(string ssoSource, string returnTo)
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
                Logger.LogInformation("Repurposing session ({Id} for SSO login", session.Id);

                // Force invalidate the logged in as user here for the session
                session.User = null;
                session.SessionVersion = 1;
            }

            SetupSessionForSSO(ssoSource, returnTo, session);

            await Database.SaveChangesAsync();

            SetSessionCookie(session, Response, useSecureCookies);

            return session;
        }

        [NonAction]
        private async Task<IActionResult> DoDiscourseLoginRedirect(string ssoType, string secret, string redirectBase,
            string returnUrlOnSuccess)
        {
            var returnUrl = new Uri(configuration.GetBaseUrl(), $"/LoginController/return/{ssoType}").ToString();

            var session = await BeginSsoLogin(ssoType, returnUrlOnSuccess);

            var payload = PrepareDiscoursePayload(session.SsoNonce, returnUrl);

            var signature = CalculateDiscourseSsoParamSignature(payload, secret);

            return Redirect(QueryHelpers.AddQueryString(
                new Uri(new Uri(redirectBase), DiscourseSsoEndpoint).ToString(),
                new Dictionary<string, string>()
                {
                    { "sso", payload },
                    { "sig", signature }
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
                    secret = configuration["Login:DevForum:SsoSecret"];
                    developer = true;
                    break;
                case SsoTypeCommunityForum:
                    secret = configuration["Login:CommunityForum:SsoSecret"];
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

                    var parsedGroups = groups.SelectMany(groupList => groupList.Split(','));

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
                requireSave = !tuple.saved;
                return tuple.result;
            }
            finally
            {
                if (requireSave)
                    await Database.SaveChangesAsync();
            }
        }

        [NonAction]
        private async Task<IActionResult> HandlePatreonSsoReturn(string state, string code)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return GetInvalidSsoParametersResult();

            var (session, result) = await FetchAndCheckSessionForSsoReturn(state, SsoTypePatreon);

            // Return in case of failure
            if (result != null)
                return result;

            bool requireSave = true;

            try
            {
                patreonAPI.Initialize(configuration["Login:Patreon:ClientId"],
                    configuration["Login:Patreon:ClientSecret"]);

                // We need to fetch the actual user email and details directly from Patreon's API
                var token = await patreonAPI.TurnCodeIntoTokens(code,
                    new Uri(configuration.GetBaseUrl(), $"/LoginController/return/{SsoTypePatreon}")
                        .ToString());

                patreonAPI.LoginAsUser(token);

                var userDetails = await patreonAPI.GetOwnDetails();

                var email = userDetails.Data.Attributes.Email;

                if (email == null)
                    throw new NullReferenceException();

                var patron = await Database.Patrons.AsQueryable().Where(p => p.Email == email).FirstOrDefaultAsync();

                if (patron == null)
                {
                    return Redirect(QueryHelpers.AddQueryString("/login", "error",
                        "You aren't a patron of Thrive Game according to our latest information. Please become our patron and try again."));
                }

                if (patron.Suspended == true)
                {
                    return Redirect(QueryHelpers.AddQueryString("/login", "error",
                        $"Your Patron status is currently suspended. Reason: {patron.SuspendedReason}"));
                }

                var patreonSettings = await Database.PatreonSettings.AsQueryable().FirstOrDefaultAsync();

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
                requireSave = !tuple.saved;
                return tuple.result;
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
        private async Task<(IActionResult result, bool saved)> HandleSsoLoginToAccount(Session session, string email,
            string username,
            string ssoType,
            bool developerLogin)
        {
            if (string.IsNullOrEmpty(email))
                return (GetInvalidSsoParametersResult(), false);

            Logger.LogInformation("Logging in SSO login user with email: {Email}", email);

            var user = await Database.Users.AsQueryable().FirstOrDefaultAsync(u => u.Email == email);

            if (user == null)
            {
                // New account needed
                Logger.LogInformation("Creating new account for SSO login: {Email} developer: {DeveloperLogin}",
                    email, developerLogin);

                user = new User()
                {
                    Email = email,
                    UserName = username,
                    Local = false,
                    SsoSource = ssoType,
                    Developer = developerLogin,
                    Admin = false
                };

                await Database.Users.AddAsync(user);
            }
            else if (user.Local)
            {
                return (Redirect(QueryHelpers.AddQueryString("/login", "error",
                    "Can't login to local account using SSO")), false);
            }
            else if (user.SsoSource != ssoType)
            {
                Logger.LogInformation(
                    "User logged in with different SSO source than before, new: {SsoType}, old: {SsoSource}", ssoType,
                    user.SsoSource);

                if (user.SsoSource == SsoTypeDevForum || user.Developer == true)
                {
                    return (Redirect(QueryHelpers.AddQueryString("/login", "error",
                            "Your account is a developer account. You need to login through the Development Forums.")),
                        false);
                }

                if (ssoType == SsoTypeDevForum)
                {
                    // Conversion to a developer account
                    await Database.LogEntries.AddAsync(new LogEntry()
                    {
                        Message = "User is now a developer due to different SSO login type",
                        TargetUser = user
                    });

                    user.Developer = true;
                    user.SsoSource = SsoTypeDevForum;
                    user.BumpUpdatedAt();
                }
                else if (user.SsoSource == SsoTypePatreon && ssoType == SsoTypeCommunityForum)
                {
                    Logger.LogInformation("Patron logged in using a community forum account");
                    await ChangeSsoSourceForNormalUser(user, ssoType);
                }
                else if (user.SsoSource == SsoTypeCommunityForum && ssoType == SsoTypePatreon)
                {
                    Logger.LogInformation("Community forum user logged in using patreon");
                    await ChangeSsoSourceForNormalUser(user, ssoType);
                }
                else
                {
                    throw new Exception("Unknown sso type (old, new) combination to move an user to");
                }
            }

            if (user.Suspended == true)
            {
                var suspension = user.SuspendedManually == true ?
                    " manually" :
                    $" with the reason: {user.SuspendedReason}";

                return (Redirect(QueryHelpers.AddQueryString("/login", "error",
                        $"Your account is suspended {suspension}")),
                    false);
            }

            var result = await FinishSsoLoginToAccount(user, session);
            return (result, true);
        }

        [NonAction]
        private async Task ChangeSsoSourceForNormalUser(User user, string ssoType)
        {
            if (user.Suspended != true || user.SuspendedManually == true || user.SsoSource == ssoType)
                return;

            // TODO: this should only unsuspend if the last login source is no longer valid
            await Database.LogEntries.AddAsync(new LogEntry()
            {
                Message = "Un-suspending automatically suspended user due to SSO source change",
                TargetUserId = user.Id,
            });

            user.Suspended = false;
            user.SsoSource = ssoType;

            await Database.SaveChangesAsync();
        }

        [NonAction]
        private async Task<IActionResult> FinishSsoLoginToAccount(User user,
            Session session)
        {
            var remoteAddress = Request.HttpContext.Connection.RemoteIpAddress;

            var sessionId = session.Id;

            Logger.LogInformation("SSO login succeeded to user id: {Id}, from: {RemoteAddress}, session: {SessionId}",
                user.Id, remoteAddress, sessionId);

            string returnUrl = session.SsoReturnUrl;

            session.User = user;
            session.LastUsed = DateTime.UtcNow;
            session.SessionVersion = user.SessionVersion;

            ClearSSOParametersFromSession(session);

            await Database.SaveChangesAsync();

            if (string.IsNullOrEmpty(returnUrl) ||
                !redirectVerifier.SanitizeRedirectUrl(returnUrl, out string redirect))
            {
                return Redirect("/");
            }
            else
            {
                return Redirect(redirect);
            }
        }
    }

    public class LoginFormData
    {
        [Required]
        public string Email { get; set; }

        [Required]
        public string Password { get; set; }

        [Required]
        public string CSRF { get; set; }

        public string ReturnUrl { get; set; }
    }

    public class SsoStartFormData
    {
        [Required]
        public string SsoType { get; set; }

        [Required]
        public string CSRF { get; set; }

        public string ReturnUrl { get; set; }
    }
}
