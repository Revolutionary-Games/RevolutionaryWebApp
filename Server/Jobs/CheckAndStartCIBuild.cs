namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared;
    using Shared.Models;
    using Utilities;
    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    public class CheckAndStartCIBuild
    {
        private readonly ILogger<CheckAndStartCIBuild> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;
        private readonly ILocalTempFileLocks localTempFileLocks;
        private readonly GithubCommitStatusReporter statusReporter;

        public CheckAndStartCIBuild(ILogger<CheckAndStartCIBuild> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient, ILocalTempFileLocks localTempFileLocks,
            GithubCommitStatusReporter statusReporter)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
            this.localTempFileLocks = localTempFileLocks;
            this.statusReporter = statusReporter;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, CancellationToken cancellationToken)
        {
            var build = await database.CiBuilds.Include(c => c.CiProject)
                .FirstOrDefaultAsync(c => c.CiProjectId == ciProjectId && c.CiBuildId == ciBuildId, cancellationToken);

            if (build == null)
            {
                logger.LogError("Failed to find CIBuild to start");
                return;
            }

            // Update our local repo copy and see what the wanted build config is
            var semaphore = localTempFileLocks.GetTempFilePath($"ciRepos/{ciProjectId}", out string tempPath);

            await semaphore.WaitAsync(cancellationToken);

            var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            CiBuildConfiguration configuration;

            try
            {
                await GitRunHelpers.EnsureRepoIsCloned(build.CiProject.RepositoryCloneUrl, tempPath, cancellationToken);

                // Checkout the ref
                await GitRunHelpers.SmartlyCheckoutRef(tempPath, build.RemoteRef, cancellationToken);

                // Clean out non-ignored files
                await GitRunHelpers.Clean(tempPath, cancellationToken);

                // Read the build configuration
                var text = await File.ReadAllTextAsync(Path.Join(tempPath, AppInfo.CIConfigurationFile), Encoding.UTF8,
                    cancellationToken);

                configuration = deserializer.Deserialize<CiBuildConfiguration>(text);
            }
            catch (Exception e)
            {
                configuration = null;
                logger.LogError("Error when trying to read repository for starting jobs: {@E}", e);
            }
            finally
            {
                semaphore.Release();
            }

            if (configuration == null)
            {
                await CreateFailedJob(build, "Failed to read repository or build configuration", cancellationToken);
                return;
            }

            // Check that configuration is valid
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(configuration, new ValidationContext(configuration), errors))
            {
                logger.LogError("Build configuration object didn't pass validations, see following errors:");

                foreach (var error in errors)
                    logger.LogError("Failure: {Error}", error);

                // TODO: pass validation errors to the build output
                await CreateFailedJob(build, "Invalid configuration yaml", cancellationToken);
                return;
            }

            // TODO: do something with the version number here...

            // Then queue the jobs we found in the configuration
            var jobs = new List<CiJob>();
            long jobId = 0;

            foreach (var jobEntry in configuration.Jobs)
            {
                if (string.IsNullOrWhiteSpace(jobEntry.Key) || jobEntry.Key.Length > 80)
                {
                    await CreateFailedJob(build, "Invalid job name in configuration", cancellationToken);
                    return;
                }

                var job = new CiJob()
                {
                    CiProjectId = ciProjectId,
                    CiBuildId = ciBuildId,
                    CiJobId = ++jobId,
                    JobName = jobEntry.Key,
                };

                await database.CiJobs.AddAsync(job, cancellationToken);
                jobs.Add(job);
            }

            await database.SaveChangesAsync(cancellationToken);

            // Send statuses to github
            foreach (var job in jobs)
            {
                if (!await statusReporter.SetCommitStatus(build.CiProject.RepositoryFullName, build.CommitHash,
                    GithubAPI.CommitStatus.Pending, statusReporter.CreateStatusUrlForJob(job), "CI checks starting",
                    job.JobName))
                {
                    logger.LogError("Failed to set commit status for a build's job: {JobName}", job.JobName);
                }
            }

            // Queue remote executor check task which will allocate a server to run the job(s) on
            jobClient.Enqueue<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None));
        }

        private async Task CreateFailedJob(CiBuild build, string failure, CancellationToken cancellationToken)
        {
            var job = new CiJob
            {
                CiProjectId = build.CiProjectId,
                CiBuildId = build.CiBuildId,
                CiJobId = 1,
                JobName = "configuration_error",
                FinishedAt = DateTime.UtcNow,
                Succeeded = false,
                State = CIJobState.Finished,

                // TODO: add failure as a build output
            };

            await database.CiJobs.AddAsync(job, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);

            if (!await statusReporter.SetCommitStatus(build.CiProject.RepositoryFullName, build.CommitHash,
                GithubAPI.CommitStatus.Failure, statusReporter.CreateStatusUrlForJob(job), failure,
                job.JobName))
            {
                logger.LogError("Failed to report serious failed commit status", job.JobName);
            }

            jobClient.Enqueue<CheckOverallBuildStatusJob>(x =>
                x.Execute(build.CiProjectId, build.CiBuildId, CancellationToken.None));
        }
    }
}
