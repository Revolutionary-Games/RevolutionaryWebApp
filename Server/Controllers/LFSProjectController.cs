using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using BlazorPagination;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Models;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class LFSProjectController : Controller
    {
        private readonly ILogger<LFSProjectController> logger;
        private readonly ApplicationDbContext context;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public LFSProjectController(ILogger<LFSProjectController> logger,
            ApplicationDbContext context,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.context = context;
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
                query = context.LfsProjects.OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException(){Value = "Invalid data selection or sort"};
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetInfo());
        }

        private async Task ReportUpdatedProject(LFSProjectInfo item)
        {
            // For now LFS list and individual LFS info pages use the same group
            await notifications.Clients.Group(NotificationGroups.LFSListUpdated).ReceiveNotification(new LFSListUpdated
                { Type = ListItemChangeType.ItemUpdated, Item = item });
        }
    }
}
