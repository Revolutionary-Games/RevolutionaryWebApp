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

            // TODO: create a separate UserInfo type to use for the list here
            return objects.ConvertResult(i => i.GetInfo(RecordAccessLevel.Admin));
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.User)]
        [HttpGet("{id:long}")]
        public async Task<ActionResult<UserInfo>> GetUser([Required] long id)
        {
            bool admin =
                HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None);

            var user = await database.Users.FindAsync(id);

            if (user == null)
                return NotFound();

            // Has to be an admin or looking at their own data
            if (!admin && HttpContext.AuthenticatedUser().Id != user.Id)
                return NotFound();

            return user.GetInfo(admin ? RecordAccessLevel.Admin : RecordAccessLevel.Private);
        }

        [NonAction]
        private async Task InvalidateUserSessions(string userId)
        {
            await notifications.Clients.User(userId).ReceiveSessionInvalidation();

            // TODO: force close signalr connections for the user https://github.com/dotnet/aspnetcore/issues/5333
        }
    }
}
