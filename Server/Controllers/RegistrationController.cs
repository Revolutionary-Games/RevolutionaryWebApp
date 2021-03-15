using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using Authorization;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class RegistrationController : Controller
    {
        private readonly ILogger<RegistrationController> logger;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;
        private readonly RegistrationStatus configuration;
        private readonly JwtTokens csrfVerifier;
        private readonly ApplicationDbContext database;

        public RegistrationController(ILogger<RegistrationController> logger,
            IHubContext<NotificationsHub, INotifications> notifications, RegistrationStatus configuration,
            JwtTokens csrfVerifier, ApplicationDbContext database)
        {
            this.logger = logger;
            this.notifications = notifications;
            this.configuration = configuration;
            this.csrfVerifier = csrfVerifier;
            this.database = database;
        }

        /// <summary>
        ///   Returns true if registration is enabled
        /// </summary>
        [HttpGet]
        public bool Get()
        {
            return configuration.RegistrationEnabled;
        }

        [HttpPost]
        public IActionResult Post(RegistrationFormData request)
        {
            if (!csrfVerifier.IsValidCSRFToken(request.CSRF))
                return BadRequest("Invalid CSRF");

            if (!SecurityHelpers.SlowEquals(request.RegistrationCode, configuration.RegistrationCode))
                return BadRequest("Invalid registration code");

            if(request.Name == null || request.Name.Length < 3)
                return BadRequest("Name is too short");

            if(request.Email == null || request.Email.Length < 3 || !request.Email.Contains('@'))
                return BadRequest("Email is invalid");

            if(request.Password == null || request.Password.Length < 6)
                return BadRequest("Password is too short");

            var password = Passwords.CreateSaltedPasswordHash(request.Password);

            return BadRequest("Not implemented");
        }
    }
}
