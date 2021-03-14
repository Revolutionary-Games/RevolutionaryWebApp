using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Authorization;
    using BlazorPagination;
    using Models;
    using Shared;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class UserManagementController : Controller
    {
        private readonly ILogger<UserManagementController> logger;
        private readonly ApplicationDbContext context;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public UserManagementController(ILogger<UserManagementController> logger,
            ApplicationDbContext context,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.context = context;
            this.notifications = notifications;
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpGet]
        public async Task<PagedResult<UserInfo>> Get(string sortColumn, SortDirection sortDirection, int page,
            int pageSize)
        {
            IQueryable<User> query;

            try
            {
                query = context.Users.OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException();
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetInfo(RecordAccessLevel.Admin));
        }

        private async Task InvalidateUserSessions(string userId)
        {
            await notifications.Clients.User(userId).ReceiveSessionInvalidation();

            // TODO: force close signalr connections for the user https://github.com/dotnet/aspnetcore/issues/5333
        }
    }
}
