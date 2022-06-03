using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers
{
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Authorization;
    using BlazorPagination;
    using Filters;
    using Hangfire;
    using Jobs;
    using Microsoft.EntityFrameworkCore;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class LFSProjectController : BaseSoftDeletedResourceController<LfsProject, LFSProjectInfo, LFSProjectDTO>
    {
        private readonly ILogger<LFSProjectController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public LFSProjectController(ILogger<LFSProjectController> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        protected override ILogger Logger => logger;
        protected override DbSet<LfsProject> Entities => database.LfsProjects;

        protected override UserAccessLevel RequiredViewAccessLevel => UserAccessLevel.NotLoggedIn;

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
                query = database.ProjectGitFiles.Where(p => p.LfsProjectId == item.Id && p.Path == path)
                    .ToAsyncEnumerable()
                    .OrderByDescending(p => p.FType).ThenBy(sortColumn, sortDirection);
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

            var objects = await database.LfsObjects.Where(l => l.LfsProjectId == item.Id)
                .OrderByDescending(l => l.Id).ToPagedResultAsync(page, pageSize);

            return objects.ConvertResult(i => i.GetDTO());
        }

        [HttpPost("{id:long}/refreshFileTree")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
        public async Task<IActionResult> RefreshFileTree([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null || item.Deleted)
                return NotFound();

            if (item.FileTreeUpdated != null && DateTime.UtcNow - item.FileTreeUpdated.Value < TimeSpan.FromMinutes(3))
                return BadRequest("This file tree was refreshed very recently");

            var user = HttpContext.AuthenticatedUserOrThrow();

            await database.ActionLogEntries.AddAsync(new ActionLogEntry()
            {
                Message = $"LFS project file tree refresh requested for {item.Id}",
                PerformedById = user.Id,
            });

            await database.SaveChangesAsync();

            jobClient.Enqueue<RefreshLFSProjectFilesJob>(x => x.Execute(item.Id, CancellationToken.None));

            logger.LogInformation("LFS project {Id} file tree refreshed by {Email}", item.Id, user.Email);
            return Ok();
        }

        [HttpPost("{id:long}/rebuildFileTree")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> RebuildFileTree([Required] long id)
        {
            var item = await FindAndCheckAccess(id);

            if (item == null || item.Deleted)
                return NotFound();

            if (item.FileTreeUpdated != null && DateTime.UtcNow - item.FileTreeUpdated.Value < TimeSpan.FromMinutes(3))
                return BadRequest("This file tree was refreshed very recently");

            var user = HttpContext.AuthenticatedUserOrThrow();

            await database.Database.BeginTransactionAsync();

            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = $"LFS project file tree rebuilt for {item.Id}",
                PerformedById = user.Id,
            });

            // Delete all entries so that they can be rebuilt
            var deleted =
                await database.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM project_git_files WHERE lfs_project_id = {item.Id};");

            item.FileTreeUpdated = null;
            item.FileTreeCommit = null;

            await database.SaveChangesAsync();
            await database.Database.CommitTransactionAsync();

            jobClient.Enqueue<RefreshLFSProjectFilesJob>(x => x.Execute(item.Id, CancellationToken.None));

            logger.LogInformation("LFS project {Id} file tree recreated by {Email}, deleted entries: {Deleted}",
                item.Id, user.Email, deleted);
            return Ok();
        }

        [HttpPut("{id:long}")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<IActionResult> UpdateLFSProject([Required] [FromBody] LFSProjectDTO request)
        {
            var item = await FindAndCheckAccess(request.Id);

            if (item == null || item.Deleted)
                return NotFound();

            var user = HttpContext.AuthenticatedUser()!;

            var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(item, request);

            if (!changes)
                return Ok();

            item.BumpUpdatedAt();

            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = $"LFS Project {item.Id} edited",

                // TODO: there could be an extra info property where the description is stored
                PerformedById = user.Id,
            });

            await database.SaveChangesAsync();

            logger.LogInformation("LFS project {Id} edited by {Email}, changes: {Description}", item.Id,
                user.Email, description);
            return Ok();
        }

        [HttpPost]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        public async Task<ActionResult> CreateNew([Required] LFSProjectDTO projectInfo)
        {
            var project = new LfsProject()
            {
                Name = projectInfo.Name,
                Slug = projectInfo.Slug,
                Public = projectInfo.Public,
                RepoUrl = projectInfo.RepoUrl,
                CloneUrl = projectInfo.CloneUrl,
                BranchToBuildFileTreeFor = projectInfo.BranchToBuildFileTreeFor,
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
            if (await database.LfsProjects.Where(p => p.Name == project.Name).AnyAsync() ||
                await database.LfsProjects.Where(p => p.Slug == project.Slug).AnyAsync())
            {
                return BadRequest("Project name or slug is already in-use");
            }

            // TODO: could maybe save the project first in order to get the ID for the log message...
            var action = new AdminAction()
            {
                Message = $"New LFS project created, slug: {project.Slug}, name: {project.Name}",
                PerformedById = HttpContext.AuthenticatedUser()!.Id
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

        protected override bool CheckExtraAccess(LfsProject project)
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

        protected override Task SaveResourceChanges(LfsProject resource)
        {
            resource.BumpUpdatedAt();
            return database.SaveChangesAsync();
        }
    }
}
