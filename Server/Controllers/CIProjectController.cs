using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.Collections.Generic;
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
    public class CIProjectController : BaseSoftDeletedResourceController<CiProject, CIProjectInfo, CIProjectDTO>
    {
        private readonly ILogger<CIProjectController> logger;
        private readonly NotificationsEnabledDb database;

        public CIProjectController(ILogger<CIProjectController> logger,
            NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        protected override ILogger Logger => logger;
        protected override DbSet<CiProject> Entities => database.CiProjects;

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpPost]
        public async Task<ActionResult> CreateNew([Required] CIProjectDTO projectInfo)
        {
            var project = new CiProject()
            {
                Name = projectInfo.Name,
                RepositoryFullName = projectInfo.RepositoryFullName,
                Public = projectInfo.Public,
                RepositoryCloneUrl = projectInfo.RepositoryCloneUrl,
                ProjectType = projectInfo.ProjectType,
                Enabled = projectInfo.Enabled,
                DefaultBranch = projectInfo.DefaultBranch
            };

            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(project, new ValidationContext(project), results, true))
            {
                return BadRequest("Invalid properties given for a new project, errors: " +
                    string.Join(", ", results.Select(r => r.ToString())));
            }

            if (!project.RepositoryFullName.Contains('/'))
                return BadRequest("Repository full name should contain a '/'");

            if (project.Name.Length < 3 || project.Name.Length > 100)
                return BadRequest("Project name is too long or too short");

            // Check for duplicate data
            if (await database.CiProjects.AsQueryable().Where(p => p.Name == project.Name).AnyAsync() ||
                await database.CiProjects.AsQueryable().Where(p => p.RepositoryFullName == project.RepositoryFullName)
                    .AnyAsync())
            {
                return BadRequest("Project name or repository full name is already in-use");
            }

            // TODO: could maybe save the project first in order to get the ID for the log message...
            var action = new AdminAction()
            {
                Message = $"New CI project created, repo: {project.RepositoryFullName}, name: {project.Name}",
                PerformedById = HttpContext.AuthenticatedUser().Id
            };

            await database.CiProjects.AddAsync(project);
            await database.AdminActions.AddAsync(action);

            try
            {
                await database.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                logger.LogWarning("Creating new CI Project failed due to db error: {@E}", e);
                return BadRequest(
                    "Error saving data to database, if the error persists the data likely violates some constraints");
            }

            return Created($"/ci/{project.Id}", project.GetDTO());
        }

        [HttpGet("{projectId:long}/builds")]
        public async Task<PagedResult<CIBuildDTO>> GetBuilds([Required] long projectId, [Required] string sortColumn,
            [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
            [Required] [Range(1, 100)] int pageSize)
        {
            var project = await FindAndCheckAccess(projectId);

            if (project == null)
            {
                throw new HttpResponseException()
                {
                    Value = "CI Project does not exist or you don't have access to it",
                };
            }

            IQueryable<CiBuild> query;

            try
            {
                query = database.CiBuilds.AsQueryable().Where(b => b.CiProjectId == projectId)
                    .OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                Logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetDTO());
        }

        [NonAction]
        protected override bool CheckExtraAccess(CiProject project)
        {
            // Only developers can see private projects
            if (!project.Public)
            {
                if (!HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Developer,
                    AuthenticationScopeRestriction.None))
                {
                    return false;
                }
            }

            return true;
        }

        [NonAction]
        protected override Task SaveResourceChanges(CiProject resource)
        {
            resource.BumpUpdatedAt();
            return database.SaveChangesAsync();
        }
    }
}
