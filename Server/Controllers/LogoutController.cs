using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared;

    [Controller]
    [Route("[controller]")]
    public class LogoutController : Controller
    {
        private readonly ILogger<LogoutController> logger;
        private readonly ApplicationDbContext database;
        private readonly ITokenVerifier csrfVerifier;

        public LogoutController(ILogger<LogoutController> logger, ApplicationDbContext database, ITokenVerifier csrfVerifier)
        {
            this.logger = logger;
            this.database = database;
            this.csrfVerifier = csrfVerifier;
        }

        [HttpPost]
        public async Task<IActionResult> Logout([FromForm] LogoutFormData request)
        {
            var existingSession = await HttpContext.Request.Cookies.GetSession(database);

            if (existingSession?.User == null)
                return BadRequest("You are not currently logged in");

            if (!csrfVerifier.IsValidCSRFToken(request.CSRF, existingSession.User))
                return BadRequest("Invalid CSRF token, please try refreshing and then try again");

            // Session version doesn't need to be enforced here as logging out a session should always be safe
            // (after the above checks)

            // TODO: if an in-progress signature exists, should the session be just converted to a logged out one?

            database.Sessions.Remove(existingSession);
            await database.SaveChangesAsync();

            logger.LogInformation("Session {Id} logged out", existingSession.Id);

            Response.Cookies.Delete(AppInfo.SessionCookieName);
            return Redirect("/login");
        }

        internal static Task PerformSessionDestroy(Session session, ApplicationDbContext database)
        {
            // TODO: could setup a hub group for each session to receive session specific messages
            database.Sessions.Remove(session);
            return database.SaveChangesAsync();
        }
    }

    public class LogoutFormData
    {
        [Required]
        public string CSRF { get; set; }
    }
}
