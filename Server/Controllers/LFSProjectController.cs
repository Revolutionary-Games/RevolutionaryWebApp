using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Models;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class LFSProjectController : Controller
    {
        private readonly ILogger<LFSProjectController> logger;
        private readonly ApplicationDbContext database;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public LFSProjectController(ILogger<LFSProjectController> logger,
            ApplicationDbContext database,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.database = database;
            this.notifications = notifications;
        }

        [HttpGet]
        public async Task<PagedResult<LFSProjectInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 50)] int pageSize)
        {
            IQueryable<LfsProject> query;

            try
            {
                query = database.LfsProjects.OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException(){Value = "Invalid data selection or sort"};
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetInfo());
        }

        [HttpGet("{id:long}")]
        public async Task<ActionResult<LFSProjectDTO>> GetSingle([Required] long id)
        {
            var item = await database.LfsProjects.FindAsync(id);

            if(item == null)
                return NotFound();

            // Only developers can see private
            if (!item.Public)
            {
                if (!HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Developer,
                    AuthenticationScopeRestriction.None))
                {
                    return NotFound();
                }
            }

            return item.GetDTO();
        }

        private async Task ReportUpdatedProject(LFSProjectInfo item)
        {
            // For now LFS list and individual LFS info pages use the same group
            await notifications.Clients.Group(NotificationGroups.LFSListUpdated).ReceiveNotification(new LFSListUpdated
                { Type = ListItemChangeType.ItemUpdated, Item = item });
        }
    }
}
