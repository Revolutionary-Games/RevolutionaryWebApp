using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Authorization;
    using Filters;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class CISecretsController : Controller
    {
        private readonly ILogger<CISecretsController> logger;
        private readonly NotificationsEnabledDb database;

        public CISecretsController(ILogger<CISecretsController> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpGet("{projectId:long}")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<ActionResult<List<CISecretDTO>>> Get([Required] long projectId, [Required] string sortColumn,
            [Required] SortDirection sortDirection)
        {
            var project = await database.CiProjects.FindAsync(projectId);

            if (project == null)
                return NotFound();

            IQueryable<CiSecret> query;

            try
            {
                query = database.CiSecrets.AsQueryable().Where(s => s.CiProjectId == project.Id)
                    .OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            return await query.Select(s => s.GetDTO()).ToListAsync();
        }
    }
}
