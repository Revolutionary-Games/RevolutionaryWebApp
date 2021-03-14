using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.Collections.Generic;
    using Shared.Models;

    [ApiController]
    [Route("LoginController")]
    public class LoginController : Controller
    {
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

        [HttpGet("start")]
        public IActionResult StartLogin(string ssoType)
        {
            throw new HttpResponseException() { Value = "Invalid SsoType" };
        }

        [HttpGet("return")]
        public IActionResult SsoReturn()
        {
            throw new HttpResponseException() { Value = "Not done..." };
        }

        [HttpPost("login")]
        public IActionResult PerformLocalLogin([FromForm] LoginFormData login)
        {
            throw new HttpResponseException() { Value = "Not done, email: " + login.Email };
        }
    }

    public class LoginFormData
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string CSRF { get; set; }
    }
}
