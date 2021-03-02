using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using System.Threading.Tasks;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class UserManagementController : Controller
    {
        private readonly ILogger<UserManagementController> logger;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public UserManagementController(ILogger<UserManagementController> logger,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.notifications = notifications;
        }

        [HttpGet]
        public void Get(string username)
        {
        }

        private async Task InvalidateUserSessions(string userId)
        {
            await notifications.Clients.User(userId).ReceiveSessionInvalidation();

            // TODO: force close signalr connections for the user https://github.com/dotnet/aspnetcore/issues/5333
        }
    }
}
