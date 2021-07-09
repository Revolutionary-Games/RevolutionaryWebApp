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
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Shared.Models.Enums;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class MeetingsController : Controller
    {
        private readonly ILogger<MeetingsController> logger;
        private readonly NotificationsEnabledDb database;

        public MeetingsController(ILogger<MeetingsController> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpGet]
        public async Task<PagedResult<MeetingInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            IQueryable<Meeting> query;

            var access = GetCurrentUserAccess();

            try
            {
                query = database.Meetings.AsQueryable().Where(m => m.ReadAccess <= access)
                    .OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);
            return objects.ConvertResult(i => i.GetInfo());
        }

        [HttpGet("{id:long}")]
        public async Task<ActionResult<MeetingDTO>> GetUser([Required] long id)
        {
            var access = GetCurrentUserAccess();

            var meeting = await database.Meetings.AsQueryable().Where(m => m.Id == id && m.ReadAccess <= access)
                .FirstOrDefaultAsync();

            if (meeting == null)
                return NotFound();

            return meeting.GetDTO();
        }

        [NonAction]
        private AssociationResourceAccess GetCurrentUserAccess()
        {
            var user = HttpContext.AuthenticatedUser();

            var access = AssociationResourceAccess.Public;

            if (user != null)
                access = user.ComputeAssociationAccessLevel();
            return access;
        }
    }
}
