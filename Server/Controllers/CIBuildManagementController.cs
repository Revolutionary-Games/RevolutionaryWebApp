using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Authorization;
    using Hangfire;
    using Jobs;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;
    using Utilities;

    /// <summary>
    ///   Controller with control operations (from the user POV) for CI builds and jobs.
    ///   Separate from the CIProjectController as that file is already big enough.
    /// </summary>
    [ApiController]
    [Route("api/v1/[controller]")]
    public class CIBuildManagementController : Controller
    {
        private readonly ILogger<CIBuildManagementController> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public CIBuildManagementController(ILogger<CIBuildManagementController> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        [HttpPost("{projectId:long}/{buildId:long}/jobs/{jobId:long}/cancel")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
        public async Task<IActionResult> CancelRunningJob([Required] long projectId,
            [Required] long buildId, [Required] long jobId)
        {
            var job = await database.CiJobs.Include(j => j.Build!).ThenInclude(b => b.CiProject)
                .Include(j => j.CiJobOutputSections)
                .FirstOrDefaultAsync(j => j.CiProjectId == projectId && j.CiBuildId == buildId && j.CiJobId == jobId);

            if (job == null)
                return NotFound();

            if (job.Build?.CiProject == null)
                throw new NotLoadedModelNavigationException();

            if (job.Build.CiProject.Deleted)
                return NotFound();

            if (job.State == CIJobState.Finished)
                return BadRequest("Can't cancel a finished job");

            long cancelSectionId = 0;

            foreach (var section in job.CiJobOutputSections)
            {
                if (section.CiJobOutputSectionId > cancelSectionId)
                    cancelSectionId = section.CiJobOutputSectionId;
            }

            await job.CreateFailureSection(database, "Job canceled by a user", "Canceled", ++cancelSectionId);

            var user = HttpContext.AuthenticatedUser()!;

            logger.LogInformation("CI job {ProjectId}-{BuildId}-{JobId} canceled by {Email}", projectId, buildId, jobId,
                user.Email);

            // TODO: would be nice to have that non-admin action log type
            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"CI job {projectId}-{buildId}-{jobId} canceled by an user",
                TargetUserId = user.Id,
            });

            await database.SaveChangesAsync();

            jobClient.Enqueue<SetFinishedCIJobStatusJob>(x =>
                x.Execute(projectId, buildId, jobId, false, CancellationToken.None));

            return Ok();
        }

        [HttpPost("{projectId:long}/{buildId:long}/rerun")]
        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
        public async Task<IActionResult> ReRunBuild([Required] long projectId,
            [Required] long buildId, bool onlyFailed = true)
        {
            var build = await database.CiBuilds.Include(b => b.CiProject).Include(b => b.CiJobs)
                .FirstOrDefaultAsync(j => j.CiProjectId == projectId && j.CiBuildId == buildId);

            if (build == null)
                return NotFound();

            if (build.CiProject == null)
                throw new NotLoadedModelNavigationException();

            if (build.CiProject.Deleted)
                return NotFound();

            if (build.Status is BuildStatus.Running or BuildStatus.GoingToFail)
                return BadRequest("Build can be rerun only after it is complete");

            var user = HttpContext.AuthenticatedUser()!;
            logger.LogInformation("CI build {ProjectId}-{BuildId} reran by {Email}", projectId, buildId, user.Email);

            // TODO: would be nice to have that non-admin action log type
            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"CI build reran {projectId}-{buildId} by a user",
                TargetUserId = user.Id,
            });

            build.Status = BuildStatus.Running;
            build.FinishedAt = null;

            // TODO: add a rerun counter to the build model

            // If there are no jobs, then the repo scan / result failed, so we might as well reset this and rerun
            // the repo scan
            if (build.CiJobs.Count < 1)
            {
                await database.SaveChangesAsync();

                jobClient.Enqueue<CheckAndStartCIBuild>(x => x.Execute(projectId, buildId, CancellationToken.None));
                return Ok("No jobs in this build, trying to re-run repo scan");
            }

            // Delete the jobs we are going to rerun
            // But first we need to grab the information from them needed to rerun the jobs (as we don't want to
            // re-check the repo here)
            long nextJobId = build.CiJobs.Max(j => j.CiJobId);
            var toRerun = build.CiJobs
                .Where(j => j.State == CIJobState.Finished && ((onlyFailed && !j.Succeeded) || !onlyFailed)).ToList();

            if (toRerun.Count < 1)
                return BadRequest("Nothing needs to rerun");

            foreach (var jobToRerun in toRerun)
            {
                var newJob = new CiJob()
                {
                    CiProjectId = jobToRerun.CiProjectId,
                    CiBuildId = jobToRerun.CiBuildId,
                    CiJobId = ++nextJobId,
                    JobName = jobToRerun.JobName,
                    Image = jobToRerun.Image,
                    CacheSettingsJson = jobToRerun.CacheSettingsJson,
                };

                await database.CiJobs.AddAsync(newJob);
            }

            // TODO: implement deleting job artifacts if those exist

            database.CiJobs.RemoveRange(toRerun);

            await database.SaveChangesAsync();

            jobClient.Enqueue<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None));
            return Ok();
        }
    }
}
