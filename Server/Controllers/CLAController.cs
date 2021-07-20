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
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class CLAController : Controller
    {
        private readonly ILogger<CLAController> logger;
        private readonly NotificationsEnabledDb database;

        public CLAController(ILogger<CLAController> logger,
            NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
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

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost]
        public async Task<IActionResult> CreateNew([Required] [FromBody] CLADTO request)
        {
            var newCla = new Cla()
            {
                Active = request.Active,
                RawMarkdown = request.RawMarkdown,
            };

            // Other active CLAs need to become inactive if new one is added
            if (newCla.Active)
            {
                foreach (var cla in await database.Clas.AsQueryable().Where(c => c.Active).ToListAsync())
                {
                    cla.Active = false;
                    logger.LogInformation("CLA {Id} is being made inactive due to creating a new one", cla.Id);
                }
            }

            await database.Clas.AddAsync(newCla);
            await database.SaveChangesAsync();

            logger.LogInformation("New CLA {Id} with active: {Active} created by {Email}", newCla.Id,
                newCla.Active, HttpContext.AuthenticatedUser().Email);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost("{id}/activate")]
        public async Task<IActionResult> Activate([Required] long id)
        {
            var cla = await database.Clas.FindAsync(id);

            if (cla == null)
                return NotFound();

            if (cla.Active)
                return BadRequest("Already active");

            // Other active CLAs need to become inactive
            foreach (var otherCla in await database.Clas.AsQueryable().Where(c => c.Active).ToListAsync())
            {
                otherCla.Active = false;
                logger.LogInformation("CLA {Id} is being made inactive due to activating {Id2}", otherCla.Id,
                    cla.Id);
            }

            cla.Active = true;
            await database.SaveChangesAsync();

            logger.LogInformation("CLA {Id} activated by {Email}", cla.Id,
                HttpContext.AuthenticatedUser().Email);

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost("{id}/deactivate")]
        public async Task<IActionResult> Deactivate([Required] long id)
        {
            var cla = await database.Clas.FindAsync(id);

            if (cla == null)
                return NotFound();

            if (!cla.Active)
                return BadRequest("CLA is not active");

            cla.Active = false;
            await database.SaveChangesAsync();

            logger.LogInformation("CLA {Id} deactivated by {Email}", cla.Id,
                HttpContext.AuthenticatedUser().Email);

            return Ok();
        }
    }
}
