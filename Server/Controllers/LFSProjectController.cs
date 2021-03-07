using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
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
        private readonly WebApiContext context;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public LFSProjectController(ILogger<LFSProjectController> logger,
            WebApiContext context,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.context = context;
            this.notifications = notifications;
        }

        [HttpGet]
        public async Task<PagedResult<LFSProjectInfo>> Get(string sortColumn, SortDirection sortDirection, int page,
            int pageSize)
        {
            var objects = await context.LfsProjects.OrderBy(sortColumn, sortDirection).ToPagedResultAsync(page, pageSize);

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
