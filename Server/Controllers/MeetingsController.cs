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

        [AuthorizeRoleFilter]
        [HttpPost]
        public async Task<IActionResult> CreateNew([Required] [FromBody] MeetingDTO request)
        {
            if (request.ReadAccess > request.JoinAccess)
                return BadRequest("Read access must not be higher than join access");

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length < 3 || request.Name.Length > 100)
                return BadRequest("Meeting name must be between 3 and 100 characters");

            if (string.IsNullOrWhiteSpace(request.Description) || request.Description.Length < 3 ||
                request.Description.Length > 10000)
            {
                return BadRequest("Description must be between 3 and 10000 characters");
            }

            if (request.ExpectedDuration != null && (request.ExpectedDuration.Value < TimeSpan.FromMinutes(1) ||
                request.ExpectedDuration.Value > TimeSpan.FromMinutes(650)))
            {
                return BadRequest($"Invalid expected duration, got value: {request.ExpectedDuration}");
            }

            if (request.JoinGracePeriod < TimeSpan.FromSeconds(0) ||
                request.JoinGracePeriod > TimeSpan.FromMinutes(200))
            {
                return BadRequest("Invalid join grace period");
            }

            if (request.StartsAt <= DateTime.UtcNow + TimeSpan.FromMinutes(1))
                return BadRequest("Can't create a meeting that would have started already");

            var access = GetCurrentUserAccess();
            if (request.JoinAccess > access)
                return BadRequest("Can't create a meeting you couldn't join due to join restriction");

            if (await database.Meetings.AsQueryable().FirstOrDefaultAsync(m => m.Name == request.Name) != null)
                return BadRequest("A meeting with that name already exists");

            var user = HttpContext.AuthenticatedUser();

            var meeting = new Meeting()
            {
                Name = request.Name,
                Description = request.Description,
                ReadAccess = request.ReadAccess,
                JoinAccess = request.JoinAccess,
                JoinGracePeriod = request.JoinGracePeriod,
                StartsAt = request.StartsAt,
                ExpectedDuration = request.ExpectedDuration,
                OwnerId = user.Id
            };

            await database.Meetings.AddAsync(meeting);

            await database.ActionLogEntries.AddAsync(new ActionLogEntry()
            {
                Message = $"New meeting ({meeting.Name}) created, scheduled to start at {meeting.StartsAt}",
                PerformedById = user.Id,
            });

            await database.SaveChangesAsync();

            logger.LogInformation("New meeting ({Id}) created by {Email}", meeting.Id, user.Email);

            return Ok();
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
