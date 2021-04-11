using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Microsoft.Extensions.Configuration;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class LFSProjectController : Controller
    {
        private readonly ILogger<LFSProjectController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IConfiguration configuration;

        public LFSProjectController(ILogger<LFSProjectController> logger,
            NotificationsEnabledDb database,
            IConfiguration configuration)
        {
            this.logger = logger;
            this.database = database;
            this.configuration = configuration;
        }

        [HttpGet]
        public async Task<PagedResult<LFSProjectInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 50)] int pageSize)
        {
            IQueryable<LfsProject> query;

            try
            {
                query = database.LfsProjects.AsQueryable().OrderBy(sortColumn, sortDirection);
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
        public async Task<ActionResult<LFSProjectDTO>> GetSingle([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            return item.GetDTO(ComputeProjectGitLFSAccessUrl(item));
        }

        [HttpGet("{id:long}/files")]
        public async Task<ActionResult<PagedResult<ProjectGitFileDTO>>> GetProjectFiles([Required] long id,
            [Required] string path,
            [Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 200)] int pageSize)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            IAsyncEnumerable<ProjectGitFile> query;

            try
            {
                query = database.ProjectGitFiles.AsQueryable()
                    .Where(p => p.LfsProjectId == item.Id && p.Path == path).ToAsyncEnumerable()
                    .OrderByDescending(p => p.Ftype).ThenBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetDTO());
        }

        [HttpGet("{id:long}/raw")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
        public async Task<ActionResult<PagedResult<LfsObjectDTO>>> GetRawObjects([Required] long id,
            [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 200)] int pageSize)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            var objects = await database.LfsObjects.AsQueryable().Where(l => l.LfsProjectId == item.Id)
                .OrderByDescending(l => l.Id).ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetDTO());
        }

        [NonAction]
        private string ComputeProjectGitLFSAccessUrl(LfsProject item)
        {
            return new Uri(new Uri(configuration["BaseUrl"]), $"/api/v1/lfs/{item.Slug}").ToString();
        }

        [NonAction]
        private async Task<LfsProject> FindAndCheckAccess(long id)
        {
            var project = await database.LfsProjects.FindAsync(id);

            if (project == null)
                return null;

            // Only developers can see private projects
            if (!project.Public)
            {
                if (!HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Developer,
                    AuthenticationScopeRestriction.None))
                {
                    return null;
                }
            }

            return project;
        }
    }
}
