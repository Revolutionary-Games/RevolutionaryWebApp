using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Microsoft.AspNetCore.Http;
    using Microsoft.EntityFrameworkCore;
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

        public LFSProjectController(ILogger<LFSProjectController> logger,
            NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        [HttpGet]
        public async Task<PagedResult<LFSProjectInfo>> Get([Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 50)] int pageSize, bool deleted = false)
        {
            // Only admins can view deleted items
            if (deleted &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None))
            {
                throw new HttpResponseException()
                    { Status = StatusCodes.Status403Forbidden, Value = "You must be an admin to view this" };
            }

            IQueryable<LfsProject> query;

            try
            {
                query = database.LfsProjects.AsQueryable().Where(p => p.Deleted == deleted)
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
        public async Task<ActionResult<LFSProjectDTO>> GetSingle([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            // Only admins can view deleted items
            if (item.Deleted &&
                !HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None))
            {
                return NotFound();
            }

            return item.GetDTO();
        }

        [HttpGet("{id:long}/files")]
        public async Task<ActionResult<PagedResult<ProjectGitFileDTO>>> GetProjectFiles([Required] long id,
            [Required] string path,
            [Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 200)] int pageSize)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null || item.Deleted)
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

            if (item == null || item.Deleted)
                return NotFound();

            var objects = await database.LfsObjects.AsQueryable().Where(l => l.LfsProjectId == item.Id)
                .OrderByDescending(l => l.Id).ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetDTO());
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost]
        public async Task<ActionResult> CreateNew([Required] LFSProjectDTO projectInfo)
        {
            var project = new LfsProject()
            {
                Name = projectInfo.Name,
                Slug = projectInfo.Slug,
                Public = projectInfo.Public,
                RepoUrl = projectInfo.RepoUrl,
                CloneUrl = projectInfo.CloneUrl
            };

            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(project, new ValidationContext(project), results, true))
            {
                return BadRequest("Invalid properties given for a new project, errors: " +
                    string.Join(", ", results.Select(r => r.ToString())));
            }

            if (!Regex.IsMatch(project.Slug, @"^[\w-]{2,15}$"))
                return BadRequest("Invalid slug, either too long, short, or uses disallowed characters");

            if (project.Name.Length < 3 || project.Name.Length > 100)
                return BadRequest("Project name is too long or too short");

            // Check for duplicate data
            if (await database.LfsProjects.AsQueryable().Where(p => p.Name == project.Name).AnyAsync() ||
                await database.LfsProjects.AsQueryable().Where(p => p.Slug == project.Slug).AnyAsync())
            {
                return BadRequest("Project name or slug is already in-use");
            }

            // TODO: could maybe save the project first in order to get the ID for the log message...
            var action = new AdminAction()
            {
                Message = $"New LFS project created, slug: {project.Slug}, name: {project.Name}",
                PerformedById = HttpContext.AuthenticatedUser().Id
            };

            await database.LfsProjects.AddAsync(project);
            await database.AdminActions.AddAsync(action);

            try
            {
                await database.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                logger.LogWarning("Creating new LFSProject failed due to db error: {@E}", e);
                return BadRequest(
                    "Error saving data to database, if the error persists the data likely violates some constraints");
            }

            return Created($"/lfs/{project.Id}", project.GetDTO());
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpDelete("{id:long}")]
        public async Task<ActionResult> DeleteProject([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            if (item.Deleted)
                return BadRequest("Resource is already deleted");

            item.Deleted = true;
            await database.SaveChangesAsync();

            return Ok();
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost("{id:long}/restore")]
        public async Task<ActionResult> RestoreProject([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null)
                return NotFound();

            if (!item.Deleted)
                return BadRequest("Resource is not deleted");

            item.Deleted = false;
            await database.SaveChangesAsync();

            return Ok();
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
