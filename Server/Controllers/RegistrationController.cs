using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Authorization;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;

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
        public async Task<IActionResult> Post(RegistrationFormData request)
        {
            if (!csrfVerifier.IsValidCSRFToken(request.CSRF, null, false))
                return BadRequest("Invalid CSRF");

            if (!SecurityHelpers.SlowEquals(request.RegistrationCode, configuration.RegistrationCode))
                return BadRequest("Invalid registration code");

            if (request.Name == null || request.Name.Length < 3)
                return BadRequest("Name is too short");

            if (request.Email == null || request.Email.Length < 3 || !request.Email.Contains('@'))
                return BadRequest("Email is invalid");

            if (request.Password == null || request.Password.Length < 6)
                return BadRequest("Password is too short");

            // Check for conflicting username or email
            if (await database.Users.FirstOrDefaultAsync(u => u.UserName == request.Name) != null ||
                await database.Users.FirstOrDefaultAsync(u => u.Email == request.Email) != null)
                return BadRequest("There is already an account associated with the given email or name");

            var password = Passwords.CreateSaltedPasswordHash(request.Password);

            var user = new User()
            {
                Email = request.Email,
                UserName = request.Name,
                PasswordHash = password,
                Local = true
            };

            await database.Users.AddAsync(user);
            await database.SaveChangesAsync();

            await notifications.Clients.Group(NotificationGroups.UserListUpdated).ReceiveNotification(
                new UserListUpdated()
                {
                    Type = ListItemChangeType.ItemAdded,
                    Item = user.GetInfo(RecordAccessLevel.Admin)
                });

            return Created($"/users/{user.Id}", user.GetInfo(RecordAccessLevel.Private));
        }
    }
}
