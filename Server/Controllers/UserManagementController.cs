using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class UserManagementController : Controller
    {
        private readonly ILogger<UserManagementController> logger;
        private readonly ApplicationDbContext database;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public UserManagementController(ILogger<UserManagementController> logger,
            ApplicationDbContext database,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.database = database;
            this.notifications = notifications;
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpGet]
        public async Task<PagedResult<UserInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            IQueryable<User> query;

            try
            {
                query = database.Users.AsQueryable().OrderBy(sortColumn, sortDirection, new[] { "UserName" });
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetInfo(RecordAccessLevel.Admin));
        }

        [NonAction]
        private async Task InvalidateUserSessions(string userId)
        {
            await notifications.Clients.User(userId).ReceiveSessionInvalidation();

            // TODO: force close signalr connections for the user https://github.com/dotnet/aspnetcore/issues/5333
        }
    }
}
