namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Models;
    using Common.Utilities;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared;
    using Shared.Models;
    using YamlDotNet.Serialization;
    using YamlDotNet.Serialization.NamingConventions;

    public class CheckAndStartCIBuild
    {
        private readonly ILogger<CheckAndStartCIBuild> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;
        private readonly ILocalTempFileLocks localTempFileLocks;
        private readonly IGithubCommitStatusReporter statusReporter;

        public CheckAndStartCIBuild(ILogger<CheckAndStartCIBuild> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient, ILocalTempFileLocks localTempFileLocks,
            IGithubCommitStatusReporter statusReporter)
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
                await GitRunHelpers.EnsureRepoIsCloned(build.CiProject.RepositoryCloneUrl, tempPath, true,
                    cancellationToken);

                // Fetch the ref
                await GitRunHelpers.FetchRef(tempPath, build.RemoteRef, cancellationToken);

                // Then checkout the commit this build is actually for
                await GitRunHelpers.Checkout(tempPath, build.CommitHash, true, cancellationToken, true);

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

            // TODO: refactor these checks to be cleaner
            if (configuration.Jobs.Select(j => j.Value.Cache).Any(c =>
                c.LoadFrom.Any(p => p.Contains("..") || p.StartsWith("/")) || c.WriteTo.Contains("..") ||
                c.WriteTo.Contains("/") ||
                c.Shared.Any(s => s.Key.Contains("..") || s.Value.Contains("..") || s.Value.Contains("/"))))
            {
                logger.LogError("Build configuration cache paths have \"..\" in them or starts with a slash");

                await CreateFailedJob(build, "Invalid configuration yaml, forbidden cache path", cancellationToken);
                return;
            }

            if (configuration.Jobs.Select(j => j.Value.Image).Any(i => i.Contains("..") || i.StartsWith("/")))
            {
                logger.LogError("Build configuration image names have \"..\" in them or starts with a slash");

                await CreateFailedJob(build, "Invalid configuration yaml, forbidden image name", cancellationToken);
                return;
            }

            if (configuration.Jobs.SelectMany(j => j.Value.Artifacts.Paths).Any(p =>
                p.Length < 3 || p.Length > 250 || p.StartsWith("/") || p.Contains("..")))
            {
                logger.LogError("Build has a too long, short, or non-relative artifact path");

                await CreateFailedJob(build, "Invalid configuration yaml, invalid artifact path(s)", cancellationToken);
                return;
            }

            if (configuration.Jobs.Any(j => j.Key == "CLA"))
            {
                logger.LogError("Build configuration job contains 'CLA' in it");

                await CreateFailedJob(build, "Invalid configuration yaml, forbidden job name", cancellationToken);
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
                    Image = jobEntry.Value.Image,
                    CacheSettingsJson = JsonSerializer.Serialize(jobEntry.Value.Cache),
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
            var outputSection = new CiJobOutputSection()
            {
                CiProjectId = build.CiProjectId,
                CiBuildId = build.CiBuildId,
                CiJobId = 1,
                CiJobOutputSectionId = 1,
                Name = "Invalid configuration",
                Status = CIJobSectionStatus.Failed,
                Output = failure
            };

            outputSection.CalculateOutputLength();

            var job = new CiJob
            {
                CiProjectId = build.CiProjectId,
                CiBuildId = build.CiBuildId,
                CiJobId = 1,
                JobName = "configuration_error",
                FinishedAt = DateTime.UtcNow,
                Succeeded = false,
                State = CIJobState.Finished,

                CiJobOutputSections = new List<CiJobOutputSection>()
                {
                    outputSection
                }
            };

            await database.CiJobs.AddAsync(job, cancellationToken);
            await database.CiJobOutputSections.AddAsync(outputSection, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);

            if (!await statusReporter.SetCommitStatus(build.CiProject.RepositoryFullName, build.CommitHash,
                GithubAPI.CommitStatus.Failure, statusReporter.CreateStatusUrlForJob(job), failure,
                job.JobName))
            {
                logger.LogError("Failed to report serious failed commit status with context {JobName}", job.JobName);
            }

            jobClient.Enqueue<CheckOverallBuildStatusJob>(x =>
                x.Execute(build.CiProjectId, build.CiBuildId, CancellationToken.None));
        }
    }
}
