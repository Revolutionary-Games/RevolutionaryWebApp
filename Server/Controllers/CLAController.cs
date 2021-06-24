using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class CLAController : Controller
    {
        private readonly ILogger<CLAController> logger;
        private readonly ApplicationDbContext database;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public CLAController(ILogger<CLAController> logger,
            ApplicationDbContext database,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.database = database;
            this.notifications = notifications;
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpGet]
        public async Task<PagedResult<CLAInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            IQueryable<Cla> query;

            try
            {
                query = database.Clas.AsQueryable().OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetInfo());
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpGet("{id:long}")]
        public async Task<ActionResult<CLADTO>> GetSingle([Required] long id)
        {
            var cla = await database.Clas.FindAsync(id);

            if (cla == null)
                return NotFound();

            return cla.GetDTO();
        }

        [HttpGet("active")]
        public async Task<ActionResult<CLADTO>> GetActive()
        {
            var cla = await database.Clas.AsQueryable().Where(c => c.Active).FirstOrDefaultAsync();

            if (cla == null)
                return NotFound();

            return cla.GetDTO();
        }
    }
}
