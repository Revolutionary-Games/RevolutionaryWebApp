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
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class LauncherLinksController : Controller
    {
        private readonly ILogger<LauncherLinksController> logger;
        private readonly NotificationsEnabledDb database;

        public LauncherLinksController(ILogger<LauncherLinksController> logger,
            NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpGet("{userId:long}")]
        [AuthorizeRoleFilter]
        public async Task<ActionResult<PagedResult<LauncherLinkDTO>>> GetLinks([Required] long userId,
            [Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 50)] int pageSize)
        {
            // Only admins can view other user's info
            if (userId != HttpContext.AuthenticatedUser().Id &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None))
            {
                return Forbid();
            }

            IQueryable<LauncherLink> query;

            try
            {
                query = database.LauncherLinks.AsQueryable().Where(l => l.UserId == userId)
                    .OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetDTO());
        }
    }
}
