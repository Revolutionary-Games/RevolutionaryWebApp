namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Common.Utilities;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Renci.SshNet.Common;
    using Services;
    using Shared;
    using Shared.Models;
    using Shared.Models.Enums;
    using Utilities;
    using FileAccess = Shared.Models.FileAccess;

    public class RunJobOnServerJob : BaseCIJobManagingJob
    {
        public const int DefaultJobConnectRetries = 15;

        private readonly IConfiguration configuration;
        private readonly IControlledServerSSHAccess controlledSSHAccess;
        private readonly IExternalServerSSHAccess externalSSHAccess;
        private readonly IGeneralRemoteDownloadUrls remoteDownloadUrls;
        private readonly IRemoteResourceHashCalculator hashCalculator;

        private readonly int cleanThreshold;

        public RunJobOnServerJob(ILogger<RunJobOnServerJob> logger, IConfiguration configuration,
            NotificationsEnabledDb database, IControlledServerSSHAccess controlledSSHAccess,
            IExternalServerSSHAccess externalSSHAccess, IBackgroundJobClient jobClient,
            IGithubCommitStatusReporter statusReporter, IGeneralRemoteDownloadUrls remoteDownloadUrls,
            IRemoteResourceHashCalculator hashCalculator) : base(logger,
            database, jobClient, statusReporter)
        {
            this.configuration = configuration;
            this.controlledSSHAccess = controlledSSHAccess;
            this.externalSSHAccess = externalSSHAccess;
            this.remoteDownloadUrls = remoteDownloadUrls;
            this.hashCalculator = hashCalculator;
            cleanThreshold = Convert.ToInt32(configuration["CI:ServerCleanUpDiskUsePercentage"]);
        }

        public async Task Execute(long ciProjectId, long ciBuildId, long ciJobId, long serverId, bool serverIsExternal,
            int retries, CancellationToken cancellationToken)
        {
            // Includes are needed here to provide fully populated data for update notifications
            var job = await Database.CiJobs.Include(j => j.Build).ThenInclude(b => b.CiProject)
                .FirstOrDefaultAsync(
                    j => j.CiProjectId == ciProjectId && j.CiBuildId == ciBuildId && j.CiJobId == ciJobId,
                    cancellationToken);

            BaseServer server;
            if (serverIsExternal)
            {
                server = await Database.ExternalServers.FindAsync(new object[] { serverId }, cancellationToken);
            }
            else
            {
                server = await Database.ControlledServers.FindAsync(new object[] { serverId }, cancellationToken);
            }

            if (server == null)
            {
                throw new ArgumentException($"Could not find server ({serverId}, external: {serverIsExternal}) " +
                    "to run build on");
            }

            if (job == null)
            {
                Logger.LogWarning("Skipping CI job as it doesn't exist");
                ReleaseServerReservation(server);
                return;
            }

            if (job.State != CIJobState.WaitingForServer)
            {
                Logger.LogWarning(
                    "CI job is not in waiting for server status, refusing to start running it on server: {ServerId}",
                    serverId);
                ReleaseServerReservation(server);
                return;
            }

            if (server.ReservedFor != job.CiJobId)
            {
                Logger.LogWarning(
                    "CI job id doesn't match reservation on server, refusing to start it on server: {ServerId}",
                    serverId);
                ReleaseServerReservation(server);
                return;
            }

            // Get the CI image for the job
            var imageFileName = job.GetImageFileName();
            var serverSideImagePath = Path.Join("CI/Images", imageFileName);

            StorageItem imageItem;
            try
            {
                imageItem = await StorageItem.FindByPath(Database, serverSideImagePath);
            }
            catch (Exception e)
            {
                // ReSharper disable once ExceptionPassedAsTemplateArgumentProblem
                Logger.LogError("Invalid image specified for CI job: {Image}, path parse exception: {@E}", job.Image,
                    e);
                job.SetFinishSuccess(false);
                await job.CreateFailureSection(Database, "Invalid image specified for job (invalid path)");
                await OnJobEnded(server, job);
                return;
            }

            if (string.IsNullOrEmpty(job.Image) || imageItem == null)
            {
                Logger.LogError("Invalid image specified for CI job: {Image}", job.Image);
                job.SetFinishSuccess(false);
                await job.CreateFailureSection(Database, "Invalid image specified for job (not found)");
                await OnJobEnded(server, job);
                return;
            }

            // The CI system uses the first valid image version. For future updates a different file name is needed
            // For example bumping the ":v1" to a ":v2" suffix
            var version = await imageItem.GetLowestUploadedVersion(Database);

            if (version == null || version.StorageFile == null)
            {
                Logger.LogError("Image with no uploaded version specified for CI job: {Image}", job.Image);
                job.SetFinishSuccess(false);
                await job.CreateFailureSection(Database, "Invalid image specified for job (not uploaded version)");
                await OnJobEnded(server, job);
                return;
            }

            // Queue a job to lock writing to the CI image if it isn't write protected yet
            if (imageItem.WriteAccess != FileAccess.Nobody)
            {
                Logger.LogInformation(
                    "Storage item {Id} used as CI image is not write locked, queuing a job to lock it", imageItem.Id);

                // To ensure the upload time is expired, this is upload time + 5 minutes
                JobClient.Schedule<LockCIImageItemJob>(x => x.Execute(imageItem.Id, CancellationToken.None),
                    AppInfo.RemoteStorageUploadExpireTime + TimeSpan.FromMinutes(5));
            }

            Logger.LogInformation("Trying to start job {CIProjectId}-{CIBuildId}-{CIJobId} on reserved " +
                "server ({Id}, {ServerIsExternal})", ciProjectId, ciBuildId, ciJobId, server.Id, serverIsExternal);

            // Try to start running the job, this can fail if the server is not actually really up yet
            IBaseSSHAccess sshAccess;
            try
            {
                if (serverIsExternal)
                {
                    externalSSHAccess.ConnectTo(server.PublicAddress.ToString(),
                        ((ExternalServer)server).SSHKeyFileName);
                    sshAccess = externalSSHAccess;
                }
                else
                {
                    controlledSSHAccess.ConnectTo(server.PublicAddress.ToString());
                    sshAccess = controlledSSHAccess;
                }
            }
            catch (SocketException)
            {
                Logger.LogInformation("Connection failed (socket exception), server is probably not up (yet)");
                await Requeue(job, retries - 1, server, serverIsExternal);
                return;
            }
            catch (SshOperationTimeoutException)
            {
                Logger.LogInformation("Connection failed (ssh timed out), server is probably not up (yet)");
                await Requeue(job, retries - 1, server, serverIsExternal);
                return;
            }

            var imageDownloadUrl =
                remoteDownloadUrls.CreateDownloadFor(version.StorageFile, AppInfo.RemoteStorageDownloadExpireTime);

            // Connection success, so now we can run the job starting on the server
            job.RunningOnServerId = serverId;
            job.RunningOnServerIsExternal = server.IsExternal;

            // TODO: permanently store on which server this job was ran on and how long since creation it took to get
            // here

            CISecretType jobSpecificSecretType = job.Build.IsSafe ? CISecretType.SafeOnly : CISecretType.UnsafeOnly;

            var secrets = await Database.CiSecrets.AsQueryable()
                .Where(s => s.CiProjectId == job.CiProjectId && (s.UsedForBuildTypes == jobSpecificSecretType ||
                    s.UsedForBuildTypes == CISecretType.All)).ToListAsync(cancellationToken);

            await PerformServerCleanUpIfNeeded(server, sshAccess);

            // Then move on to the build starting, first thing is to download the CI executor script
            // TODO: is there a possibility that this is not secure? Someone would need to do HTTPS MItM attack...

            var executorDownload = GetUrlToDownloadCIExecutor();
            var executorResourceDownload = GetUrlToDownloadCIExecutorResource();
            var executorHash = await hashCalculator.Sha256(executorDownload, cancellationToken);
            var posixHelperHash = await hashCalculator.Sha256(executorResourceDownload, cancellationToken);

            // TODO: using async would be nice for the run commands when supported
            var result1 = sshAccess
                .RunCommand("set -e\n" +
                    CreateDownloadCommand("~/CIExecutor", executorHash, executorDownload) +
                    CreateDownloadCommand("~/libMonoPosixHelper.so", posixHelperHash,
                        executorResourceDownload) + "chmod +x ~/CIExecutor");

            if (!result1.Success)
                throw new Exception($"Failed to run executor download step: {result1.Result}, error: {result1.Error}");

            // This save is done here as the build status might get reported back to us before we finish with the ssh
            // commands
            job.State = CIJobState.Running;
            await Database.SaveChangesAsync(cancellationToken);

            // and then run it with environment variables for this build

            // Remove all type secrets if there is one with the same name that is build specific
            var cleanedSecrets = secrets
                .Where(s => s.UsedForBuildTypes != CISecretType.All || !secrets.Any(s2 =>
                    s2.SecretName == s.SecretName && s2.UsedForBuildTypes != s.UsedForBuildTypes))
                .Select(s => s.ToExecutorData());

            var env = new StringBuilder(250);
            env.Append("export CI_REF=\"");
            env.Append(BashEscape.EscapeForBash(job.Build.RemoteRef));
            env.Append("\"; export CI_COMMIT_HASH=\"");
            env.Append(BashEscape.EscapeForBash(job.Build.CommitHash));
            env.Append("\"; export CI_EARLIER_COMMIT=\"");
            env.Append(BashEscape.EscapeForBash(job.Build.PreviousCommit));
            env.Append("\"; export CI_BRANCH=\"");
            env.Append(BashEscape.EscapeForBash(job.Build.Branch));
            env.Append("\"; export CI_DEFAULT_BRANCH=\"");
            env.Append(BashEscape.EscapeForBash(job.Build.CiProject.DefaultBranch));
            env.Append("\"; export CI_TRUSTED=\"");
            env.Append(job.Build.IsSafe);
            env.Append("\"; export CI_ORIGIN=\"");
            env.Append(BashEscape.EscapeForBash(job.Build.CiProject.RepositoryCloneUrl));
            env.Append("\"; export CI_IMAGE_DL_URL=\"");
            env.Append(BashEscape.EscapeForBash(imageDownloadUrl));
            env.Append("\"; export CI_IMAGE_NAME=\"");
            env.Append(BashEscape.EscapeForBash(job.Image));
            env.Append("\"; export CI_IMAGE_FILENAME=\"");
            env.Append(BashEscape.EscapeForBash(imageFileName));
            env.Append("\"; export CI_CACHE_OPTIONS=\"");
            env.Append(BashEscape.EscapeForBash(job.CacheSettingsJson));
            env.Append("\"; export CI_SECRETS=\"");
            env.Append(BashEscape.EscapeForBash(JsonSerializer.Serialize(cleanedSecrets)));
            env.Append("\"; export CI_JOB_NAME=\"");
            env.Append(BashEscape.EscapeForBash(job.JobName));
            env.Append("\";");

            var result2 =
                sshAccess.RunCommand($"{env} nohup ~/CIExecutor {GetConnectToUrl(job)} > " +
                    "build_script_output.txt 2>&1 &");

            if (!result2.Success)
            {
                throw new Exception($"Failed to start running CI executor: {result2.Result}, error: {result2.Error}");
            }

            JobClient.Schedule<CheckCIJobOutputHasConnectedJob>(
                x => x.Execute(ciProjectId, ciBuildId, ciJobId, serverId, CancellationToken.None),
                TimeSpan.FromMinutes(5));

            JobClient.Schedule<CancelCIBuildIfStuckJob>(
                x => x.Execute(ciProjectId, ciBuildId, ciJobId, serverId, server.IsExternal, CancellationToken.None),
                TimeSpan.FromMinutes(61));

            Logger.LogInformation(
                "CI job startup succeeded, now it's up to the executor to contact us with updates");
        }

        private async Task PerformServerCleanUpIfNeeded(BaseServer server, IBaseSSHAccess sshAccess)
        {
            int? detectedUsedPercentage = null;

            // Check server remaining disk space here
            var diskSpaceResult = sshAccess.RunCommand(DiskUsageCheckCommand);

            if (!diskSpaceResult.Success)
            {
                throw new Exception(
                    $"Failed to check server disk space: {diskSpaceResult.Result}, error: {diskSpaceResult.Error}");
            }

            foreach (var line in diskSpaceResult.Result.Split('\n'))
            {
                var match = Regex.Match(line, @"(\S+)\s+(\S+)\s+(\S+)\s+(\S+)\s+(\d+)%\s+(\S+)\s*");

                if (!match.Success)
                    continue;

                var groups = match.Groups;

                // Skip if doesn't start with /dev
                if (!groups[1].Captures[0].Value.StartsWith("/dev/"))
                    continue;

                var percentage = Convert.ToInt32(groups[5].Captures[0].Value);

                // Immediately return if we found the root file system
                if (groups[6].Captures[0].Value == "/")
                {
                    detectedUsedPercentage = percentage;
                    break;
                }

                detectedUsedPercentage = percentage;
            }

            if (detectedUsedPercentage == null)
            {
                Logger.LogWarning("Could not detect free disk space from: {Result}", diskSpaceResult.Result);
                detectedUsedPercentage = -1;
            }

            server.UsedDiskSpace = detectedUsedPercentage.Value;

            if (server.CleanUpQueued || server.UsedDiskSpace > cleanThreshold)
            {
                if (server.CleanUpQueued)
                {
                    Logger.LogInformation("Cleaning server {Id} because clean up was manually requested", server.Id);
                }
                else
                {
                    Logger.LogInformation(
                        "Cleaning server {Id} because it's disk use is: {UsedDiskSpace}% (> {CleanThreshold}%)",
                        server.Id, server.UsedDiskSpace, cleanThreshold);
                }

                await PerformServerCleanUp(server, sshAccess);
                server.CleanUpQueued = false;
            }

            await Database.SaveChangesAsync();
        }

        private async Task PerformServerCleanUp(BaseServer server, IBaseSSHAccess sshAccess)
        {
            var start = DateTime.UtcNow;

            // This deletes everything (maybe sometimes it would be better to leave the downloaded images alone)
            // "-f" is very important here to prevent this just getting locked up forever
            var cleanupResult =
                sshAccess.RunCommand(
                    "sudo rm -rf /executor_cache/* && rm -rf ~/images && podman system reset -f");

            if (!cleanupResult.Success)
            {
                Logger.LogError("Failed to cleanup on server: {Result}, error: {Error}", cleanupResult.Result,
                    cleanupResult.Error);

                await Database.LogEntries.AddAsync(new LogEntry()
                {
                    Message = $"Failed to cleanup server {server.Id}"
                });
            }

            var elapsed = DateTime.UtcNow - start;
            Logger.LogInformation("Completed cleanup for server {Id}, elapsed: {Elapsed}", server.Id, elapsed);
        }

        private async Task Requeue(CiJob job, int retries, BaseServer server, bool serverIsExternal)
        {
            if (retries < 1)
            {
                Logger.LogError("CI build ran out of tries to try starting on the server");
                job.State = CIJobState.Finished;
                job.FinishedAt = DateTime.UtcNow;
                job.Succeeded = false;

                await job.CreateFailureSection(Database, "Connection to build server failed after multiple retries",
                    "Server Failure");

                // Stop hogging the server and mark the build as failed
                await OnJobEnded(server, job);
                return;
            }

            JobClient.Schedule<RunJobOnServerJob>(x =>
                    x.Execute(job.CiProjectId, job.CiBuildId, job.CiJobId, server.Id, serverIsExternal, retries,
                        CancellationToken.None),
                TimeSpan.FromSeconds(10));
        }

        private string GetUrlToDownloadCIExecutor()
        {
            return new Uri(configuration.GetBaseUrl(), "/CIExecutor").ToString();
        }

        private string GetUrlToDownloadCIExecutorResource()
        {
            return new Uri(configuration.GetBaseUrl(), "/libMonoPosixHelper.so").ToString();
        }

        private string GetConnectToUrl(CiJob job)
        {
            return new Uri(configuration.GetBaseUrl(), $"/ciBuildConnection?key={job.BuildOutputConnectKey}")
                .ToString();
        }

        private static string CreateDownloadCommand(string filePath, string hash, string downloadUrl)
        {
            return $"if [ ! -f {filePath} -o '{hash}' != \"$(sha256sum {filePath} | awk '{{print $1}}')\" ]; then\n" +
                $"echo 'downloading missing or incorrect hash file: {filePath}'\n" +
                $"rm -f {filePath} && curl -L {downloadUrl} -o {filePath}\n" +
                "fi\n";
        }
    }
}
