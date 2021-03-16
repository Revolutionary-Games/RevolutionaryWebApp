using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.Collections.Generic;
    using System.Text.Encodings.Web;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.AspNetCore.WebUtilities;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    [ApiController]
    [Route("LoginController")]
    public class LoginController : Controller
    {
        private readonly ILogger<LoginController> logger;
        private readonly ApplicationDbContext database;
        private readonly JwtTokens csrfVerifier;

        public LoginController(ILogger<LoginController> logger, ApplicationDbContext database, JwtTokens csrfVerifier)
        {
            this.logger = logger;
            this.database = database;
            this.csrfVerifier = csrfVerifier;
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
                                InternalName = "devforum",
                                Active = true
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
                                Active = true
                            },
                            new()
                            {
                                ReadableName = "Login Using Patreon",
                                InternalName = "patreon",
                                Active = false
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
                                Active = true,
                                Local = true
                            }
                        }
                    },
                }
            };
        }

        [HttpPost("start")]
        public async Task<IActionResult> StartLogin(string ssoType, [FromBody] string csrf)
        {
            await PerformPreLoginChecks(csrf);

            throw new HttpResponseException() { Value = "Invalid SsoType" };
        }

        [HttpGet("return")]
        public IActionResult SsoReturn()
        {
            throw new HttpResponseException() { Value = "Not done..." };
        }

        [HttpPost("login")]
        public async Task<IActionResult> PerformLocalLogin([FromForm] LoginFormData login)
        {
            await PerformPreLoginChecks(login.CSRF);

            return Redirect(QueryHelpers.AddQueryString("/login", "error", "Invalid username or password"));
        }

        private async Task PerformPreLoginChecks(string csrf)
        {
            var existingSession = await HttpContext.Request.Cookies.GetSession(database);

            // TODO: verify that the client making the request had up to date token
            if (!csrfVerifier.IsValidCSRFToken(csrf))
                throw new HttpResponseException() { Value = "Invalid CSRF token. Please refresh and try logging in again" };

            // If there is an existing session, end it
            if (existingSession != null)
            {
                logger.LogInformation("Destroying an existing session before starting login");
                await LogoutController.PerformSessionDestroy(existingSession, database);
            }
        }
    }

    public class LoginFormData
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string CSRF { get; set; }
    }
}
