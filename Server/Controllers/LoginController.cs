using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
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
    public class LoginController : Controller
    {
        private readonly ILogger<LoginController> logger;
        private readonly ApplicationDbContext database;
        private readonly IConfiguration configuration;
        private readonly JwtTokens csrfVerifier;
        private readonly RedirectVerifier redirectVerifier;

        private readonly bool localLoginEnabled;

        public LoginController(ILogger<LoginController> logger, ApplicationDbContext database,
            IConfiguration configuration, JwtTokens csrfVerifier,
            RedirectVerifier redirectVerifier)
        {
            this.logger = logger;
            this.database = database;
            this.configuration = configuration;
            this.csrfVerifier = csrfVerifier;
            this.redirectVerifier = redirectVerifier;

            localLoginEnabled = Convert.ToBoolean(configuration["Login:Local:Enabled"]);
        }

        private bool DevForumConfigured => !string.IsNullOrEmpty(configuration["Login:DevForum:SsoSecret"]);
        private bool CommunityForumConfigured => !string.IsNullOrEmpty(configuration["Login:CommunityForum:SsoSecret"]);

        private bool PatreonConfigured => !string.IsNullOrEmpty(configuration["Login:Patreon:ClientId"]) &&
            !string.IsNullOrEmpty(configuration["Login:Patreon:ClientSecret"]);

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
                                InternalName = "devforum",
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
                                InternalName = "communityforum",
                                Active = CommunityForumConfigured
                            },
                            new()
                            {
                                ReadableName = "Login Using Patreon",
                                InternalName = "patreon",
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
        public async Task<IActionResult> StartLogin([Required] string ssoType, [FromBody] [Required] string csrf)
        {
            await PerformPreLoginChecks(csrf);

            switch (ssoType)
            {
                case "devforum":
                {
                    if (!DevForumConfigured)
                        return CreateResponseForDisabledOption();

                    break;
                }
                case "communityforum":
                {
                    if (!CommunityForumConfigured)
                        return CreateResponseForDisabledOption();

                    break;
                }
                case "patreon":
                {
                    if (!PatreonConfigured)
                        return CreateResponseForDisabledOption();

                    break;
                }
            }

            return Redirect(QueryHelpers.AddQueryString("/login", "error", "Invalid SsoType"));
        }

        [HttpGet("return")]
        public IActionResult SsoReturn()
        {
            throw new HttpResponseException() { Value = "Not done..." };
        }

        [HttpPost("login")]
        public async Task<IActionResult> PerformLocalLogin([FromForm] LoginFormData login)
        {
            if (!localLoginEnabled)
                return CreateResponseForDisabledOption();

            await PerformPreLoginChecks(login.CSRF);

            var user = await database.Users.FirstOrDefaultAsync(u => u.Email == login.Email && u.Local);

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
        private IActionResult CreateResponseForDisabledOption()
        {
            return Redirect(QueryHelpers.AddQueryString("/login", "error", "This login option is not enabled"));
        }

        [NonAction]
        private async Task PerformPreLoginChecks(string csrf)
        {
            var existingSession = await HttpContext.Request.Cookies.GetSession(database);

            // TODO: verify that the client making the request had up to date token
            if (!csrfVerifier.IsValidCSRFToken(csrf, existingSession?.User))
            {
                throw new HttpResponseException()
                    { Value = "Invalid CSRF token. Please refresh and try logging in again" };
            }

            // If there is an existing session, end it
            if (existingSession != null)
            {
                logger.LogInformation("Destroying an existing session before starting login");
                await LogoutController.PerformSessionDestroy(existingSession, database);
            }
        }

        [NonAction]
        private async Task BeginNewSession(User user)
        {
            var remoteAddress = Request.HttpContext.Connection.RemoteIpAddress;

            var session = new Session
            {
                User = user, SessionVersion = user.SessionVersion, LastUsedFrom = remoteAddress
            };

            await database.Sessions.AddAsync(session);
            await database.SaveChangesAsync();

            logger.LogInformation("Successful login for user {Email} from {RemoteAddress}, session: {Id}", user.Email,
                remoteAddress, session.Id);

            var options = new CookieOptions
            {
                Expires = DateTime.UtcNow.AddSeconds(AppInfo.SessionExpirySeconds),
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,

                // TODO: do we need to set the domain explicitly?
                // options.Domain;

                // This might cause issues when locally testing with Chrome
                Secure = true,

                // Sessions are used for logins, they are essential. This might need to be re-thought out if
                // non-essential info is attached to sessions later
                IsEssential = true
            };

            Response.Cookies.Append(AppInfo.SessionCookieName, session.Id.ToString(), options);
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
}
