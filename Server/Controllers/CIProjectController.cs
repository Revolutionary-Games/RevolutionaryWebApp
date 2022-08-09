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
        private const string BuildDoesNotExistError = "CI Build does not exist or you don't have access to it";
        private const string JobDoesNotExistError = "CI Job does not exist or you don't have access to it";

        private readonly ILogger<CIProjectController> logger;
        private readonly NotificationsEnabledDb database;

        public CIProjectController(ILogger<CIProjectController> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        protected override ILogger Logger => logger;
        protected override DbSet<CiProject> Entities => database.CiProjects;

        protected override UserAccessLevel RequiredViewAccessLevel => UserAccessLevel.NotLoggedIn;

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
                DefaultBranch = projectInfo.DefaultBranch,
            };

            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(project, new ValidationContext(project), results, true))
            {
                return BadRequest("Invalid properties given for a new project, errors: " +
                    string.Join(", ", results.Select(r => r.ToString())));
            }

            if (!project.RepositoryFullName.Contains('/'))
                return BadRequest("Repository full name should contain a '/'");

            if (project.Name.Length is < 3 or > 100)
                return BadRequest("Project name is too long or too short");

            // Check for duplicate data
            if (await database.CiProjects.Where(p => p.Name == project.Name).AnyAsync() ||
                await database.CiProjects.Where(p => p.RepositoryFullName == project.RepositoryFullName).AnyAsync())
            {
                return BadRequest("Project name or repository full name is already in-use");
            }

            // TODO: could maybe save the project first in order to get the ID for the log message...
            var action = new AdminAction()
            {
                Message = $"New CI project created, repo: {project.RepositoryFullName}, name: {project.Name}",
                PerformedById = HttpContext.AuthenticatedUser()!.Id,
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
        public async Task<ActionResult<PagedResult<CIBuildDTO>>> GetBuilds([Required] long projectId,
            [Required] string sortColumn, [Required] SortDirection sortDirection,
            [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
        {
            var project = await FindAndCheckAccess(projectId);

            if (project == null || project.Deleted)
                return NotFound("CI Project does not exist or you don't have access to it");

            IQueryable<CiBuild> query;

            try
            {
                query = database.CiBuilds.Where(b => b.CiProjectId == projectId).OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                Logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            var objects = await query.ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetDTO());
        }

        [HttpGet("{projectId:long}/builds/{buildId:long}")]
        public async Task<ActionResult<CIBuildDTO>> GetBuild([Required] long projectId,
            [Required] long buildId)
        {
            var item = await database.CiBuilds.Include(b => b.CiProject)
                .FirstOrDefaultAsync(b => b.CiProjectId == projectId && b.CiBuildId == buildId);

            if (item == null)
                return NotFound(BuildDoesNotExistError);

            if (item.CiProject == null)
                throw new NotLoadedModelNavigationException();

            if (!CheckExtraAccess(item.CiProject) || item.CiProject.Deleted)
                return NotFound("CI Build does not exist or you don't have access to it");

            return item.GetDTO();
        }

        [HttpGet("{projectId:long}/builds/{buildId:long}/jobs")]
        public async Task<ActionResult<PagedResult<CIJobDTO>>> GetJobs([Required] long projectId,
            [Required] long buildId, [Required] string sortColumn, [Required] SortDirection sortDirection,
            [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 50)] int pageSize)
        {
            var build = await database.CiBuilds.Include(b => b.CiProject)
                .FirstOrDefaultAsync(b => b.CiProjectId == projectId && b.CiBuildId == buildId);

            if (build == null)
                return NotFound(BuildDoesNotExistError);

            if (build.CiProject == null)
                throw new NotLoadedModelNavigationException();

            if (!CheckExtraAccess(build.CiProject) || build.CiProject.Deleted)
                return NotFound("CI Build does not exist or you don't have access to it");

            IQueryable<CiJob> query;

            try
            {
                query = database.CiJobs.Where(j => j.CiProjectId == projectId && j.CiBuildId == buildId)
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

        [HttpGet("{projectId:long}/builds/{buildId:long}/jobs/{jobId:long}")]
        public async Task<ActionResult<CIJobDTO>> GetJob([Required] long projectId,
            [Required] long buildId, [Required] long jobId)
        {
            var item = await database.CiJobs.Include(j => j.Build!).ThenInclude(b => b.CiProject)
                .FirstOrDefaultAsync(j => j.CiProjectId == projectId && j.CiBuildId == buildId && j.CiJobId == jobId);

            if (item == null)
                return NotFound(JobDoesNotExistError);

            if (item.Build?.CiProject == null)
                throw new NotLoadedModelNavigationException();

            if (!CheckExtraAccess(item.Build.CiProject) || item.Build.CiProject.Deleted)
                return NotFound(JobDoesNotExistError);

            return item.GetDTO();
        }

        [HttpGet("{projectId:long}/builds/{buildId:long}/jobs/{jobId:long}/output")]
        public async Task<ActionResult<List<CIJobOutputSectionInfo>>> GetJobOutputSections([Required] long projectId,
            [Required] long buildId, [Required] long jobId, [Required] string sortColumn,
            [Required] SortDirection sortDirection)
        {
            var job = await database.CiJobs.Include(j => j.Build!).ThenInclude(b => b.CiProject)
                .FirstOrDefaultAsync(j => j.CiProjectId == projectId && j.CiBuildId == buildId && j.CiJobId == jobId);

            if (job == null)
                return NotFound(JobDoesNotExistError);

            if (job.Build?.CiProject == null)
                throw new NotLoadedModelNavigationException();

            if (!CheckExtraAccess(job.Build.CiProject) || job.Build.CiProject.Deleted)
                return NotFound(JobDoesNotExistError);

            IQueryable<CiJobOutputSection> query;

            try
            {
                query = database.CiJobOutputSections.Where(s =>
                        s.CiProjectId == projectId && s.CiBuildId == buildId && s.CiJobId == jobId)
                    .OrderBy(sortColumn, sortDirection);
            }
            catch (ArgumentException e)
            {
                Logger.LogWarning("Invalid requested order: {@E}", e);
                throw new HttpResponseException() { Value = "Invalid data selection or sort" };
            }

            // Exclude the output from fetch in DB which might be very large
            var objects = await query.Select(s => new
                CiJobOutputSection
                {
                    CiProjectId = s.CiProjectId,
                    CiBuildId = s.CiBuildId,
                    CiJobId = s.CiJobId,
                    CiJobOutputSectionId = s.CiJobOutputSectionId,
                    Name = s.Name,
                    Status = s.Status,
                    OutputLength = s.OutputLength,
                    StartedAt = s.StartedAt,
                    FinishedAt = s.FinishedAt,
                }).ToListAsync();

            return objects.Select(o => o.GetInfo()).ToList();
        }

        [HttpGet("{projectId:long}/builds/{buildId:long}/jobs/{jobId:long}/output/{sectionId:long}")]
        public async Task<ActionResult<CIJobOutputSectionDTO>> GetJobOutputSection([Required] long projectId,
            [Required] long buildId, [Required] long jobId, [Required] long sectionId)
        {
            var job = await database.CiJobs.Include(j => j.Build!).ThenInclude(b => b.CiProject)
                .FirstOrDefaultAsync(j => j.CiProjectId == projectId && j.CiBuildId == buildId && j.CiJobId == jobId);

            if (job == null)
                return NotFound(JobDoesNotExistError);

            if (job.Build?.CiProject == null)
                throw new NotLoadedModelNavigationException();

            if (!CheckExtraAccess(job.Build.CiProject) || job.Build.CiProject.Deleted)
                return NotFound("CI Job does not exist or you don't have access to it");

            var section = await database.CiJobOutputSections.FirstOrDefaultAsync(s =>
                s.CiProjectId == projectId && s.CiBuildId == buildId && s.CiJobId == jobId && s.CiJobOutputSectionId ==
                sectionId);

            if (section == null)
                return NotFound("No output section with specified id exists");

            return section.GetDTO();
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
