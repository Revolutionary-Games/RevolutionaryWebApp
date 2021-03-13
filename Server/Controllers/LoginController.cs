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
                Options = new List<LoginOption>()
                {
                    new LoginOption()
                    {
                        ReadableName = "Login Using Development Forum Account",
                        InternalName = "devforum"
                    }
                }
            };
        }
    }
}
