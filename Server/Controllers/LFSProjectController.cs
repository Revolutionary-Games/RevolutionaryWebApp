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
    using Shared;
    using Shared.Notifications;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class LFSProjectController : Controller
    {
        private readonly ILogger<LFSProjectController> logger;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public LFSProjectController(ILogger<LFSProjectController> logger,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.notifications = notifications;
        }

        [HttpGet]
        public PagedResult<LFSProjectInfo> Get(string sortColumn, SortDirection sortDirection, int page,
            int pageSize)
        {
            var rng = new Random();

            var result = Enumerable.Range(1, 122).Select(index => new LFSProjectInfo()
            {
                Name = "Project_" + index,
                Public = true,
                Size = index * 50,
                LastUpdated = DateTime.Now + TimeSpan.FromSeconds(rng.Next(-20, 55)),
                CreatedAt = DateTime.Now - TimeSpan.FromSeconds(rng.Next(1000, 10000)),
            }).AsQueryable().OrderBy(sortColumn, sortDirection);

            // TODO: remove
            notifications.Clients.All.ReceiveSiteNotice(SiteNoticeType.Primary, "page opened: " + page).Wait();

            // notifications.Clients.All.ReceiveNotification(result.First()).Wait();
            notifications.Clients.All.ReceiveNotification(
                new LFSProjectInfo()
                {
                    Name = "something",
                    Public = true,
                    Size = 123,
                    CreatedAt = DateTime.Parse("2021-01-02T06:12:00"),
                    LastUpdated = DateTime.Parse("2021-01-02T06:15:00"),
                }
            ).Wait();

            // await ReportUpdatedProject(result.First());

            return result.ToPagedResult(page, pageSize);
        }

        private async Task ReportUpdatedProject(LFSProjectInfo item)
        {
            // For now LFS list and individual LFS info pages use the same group
            // await notifications.Clients.Group(NotificationGroups.LFSListUpdated).ReceiveNotification(item);
            await notifications.Clients.All.ReceiveNotification(item);
        }
    }
}
