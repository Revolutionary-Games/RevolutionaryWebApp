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
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
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
            if (userId != HttpContext.AuthenticatedUser()!.Id &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None))
            {
                return Forbid();
            }

            IQueryable<LauncherLink> query;

            try
            {
                query = database.LauncherLinks.Where(l => l.UserId == userId)
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

        [HttpDelete("{userId:long}")]
        [AuthorizeRoleFilter]
        public async Task<IActionResult> DeleteAllLinks([Required] long userId)
        {
            var performingUser = HttpContext.AuthenticatedUser()!;

            // Only admins can delete other user's links
            if (userId != performingUser.Id &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None))
            {
                return Forbid();
            }

            var linksToDelete = await database.LauncherLinks.Where(l => l.UserId == userId).ToListAsync();

            // Skip doing anything if there's nothing to delete
            if (linksToDelete.Count < 1)
                return Ok();

            if (userId == performingUser.Id)
            {
                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message = "All launcher links deleted by self",
                    TargetUserId = userId
                });
            }
            else
            {
                await database.AdminActions.AddAsync(new AdminAction()
                {
                    Message = "All launcher links deleted by an admin",
                    TargetUserId = userId,
                    PerformedById = performingUser.Id
                });
            }

            database.LauncherLinks.RemoveRange(linksToDelete);

            await database.SaveChangesAsync();

            return Ok();
        }

        [HttpDelete("{userId:long}/{linkId:long}")]
        [AuthorizeRoleFilter]
        public async Task<IActionResult> DeleteSpecificLink([Required] long userId, [Required] long linkId)
        {
            var performingUser = HttpContext.AuthenticatedUser()!;

            // Only admins can delete other user's links
            if (userId != performingUser.Id &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None))
            {
                return Forbid();
            }

            var linkToDelete =
                await database.LauncherLinks.FirstOrDefaultAsync(l => l.Id == linkId && l.UserId == userId);

            if (linkToDelete == null)
                return NotFound("Link with the given ID not found, or it doesn't belong to the target user");

            if (userId == performingUser.Id)
            {
                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message = $"Launcher link ({linkId}) deleted by owning user",
                    TargetUserId = userId
                });
            }
            else
            {
                await database.AdminActions.AddAsync(new AdminAction()
                {
                    Message = $"Launcher link ({linkId}) for user deleted by an admin",
                    TargetUserId = userId,
                    PerformedById = performingUser.Id
                });
            }

            database.LauncherLinks.Remove(linkToDelete);

            await database.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        [AuthorizeRoleFilter]
        public async Task<IActionResult> CreateLinkCode()
        {
            var user = HttpContext.AuthenticatedUser()!;

            // Fail if too many links
            if (await database.LauncherLinks.CountAsync(l => l.UserId == user.Id) >= AppInfo.DefaultMaxLauncherLinks)
            {
                return BadRequest("You already have the maximum number of launchers linked");
            }

            var modifiableUser = await database.Users.FindAsync(user.Id);

            if (modifiableUser == null)
            {
                throw new HttpResponseException()
                    { Status = StatusCodes.Status500InternalServerError, Value = "Failed to find target user" };
            }

            modifiableUser.LauncherLinkCode = Guid.NewGuid().ToString();
            modifiableUser.LauncherCodeExpires = DateTime.UtcNow + AppInfo.LauncherLinkCodeExpireTime;

            await database.SaveChangesAsync();

            logger.LogInformation("User {Email} started linking a new launcher (code created)", user.Email);

            return Ok(modifiableUser.LauncherLinkCode);
        }
    }
}
