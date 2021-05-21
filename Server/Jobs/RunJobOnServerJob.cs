namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.IO;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Renci.SshNet.Common;
    using Services;
    using Shared;
    using Shared.Models;
    using Utilities;
    using FileAccess = Shared.Models.FileAccess;

    public class RunJobOnServerJob : BaseCIJobManagingJob
    {
        public const int DefaultJobConnectRetries = 30;

        private readonly IConfiguration configuration;
        private readonly ControlledServerSSHAccess sshAccess;
        private readonly GeneralRemoteDownloadUrls remoteDownloadUrls;

        public RunJobOnServerJob(ILogger<RunJobOnServerJob> logger, IConfiguration configuration,
            NotificationsEnabledDb database, ControlledServerSSHAccess sshAccess, IBackgroundJobClient jobClient,
            GithubCommitStatusReporter statusReporter, GeneralRemoteDownloadUrls remoteDownloadUrls) : base(logger,
            database,
            jobClient, statusReporter)
        {
            this.configuration = configuration;
            this.sshAccess = sshAccess;
            this.remoteDownloadUrls = remoteDownloadUrls;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, long ciJobId, long serverId, int retries,
            CancellationToken cancellationToken)
        {
            // Includes are needed here to provide fully populated data for update notifications
            var job = await Database.CiJobs.Include(j => j.Build).ThenInclude(b => b.CiProject)
                .FirstOrDefaultAsync(
                    j => j.CiProjectId == ciProjectId && j.CiBuildId == ciBuildId && j.CiJobId == ciJobId,
                    cancellationToken);
            var server = await Database.ControlledServers.FindAsync(new object[] { serverId }, cancellationToken);

            if (server == null)
                throw new ArgumentException("Could not find server to run build on");

            if (job == null)
            {
                ReleaseServerReservation(server);
                Logger.LogWarning("Skipping CI job as it doesn't exist");
                return;
            }

            // TODO: check if ciJobId matches the reservation on the server?

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

            Logger.LogInformation("Trying to start job {CIProjectId}-{CIBuildId}-{CIJobId} on reserved server",
                ciProjectId, ciBuildId, ciJobId);

            // Try to start running the job, this can fail if the server is not actually really up yet
            try
            {
                sshAccess.ConnectTo(server.PublicAddress.ToString());
            }
            catch (SocketException)
            {
                Logger.LogInformation("Connection failed (socket exception), server is probably not up yet");
                await Requeue(job, retries - 1, server);
                return;
            }
            catch (SshOperationTimeoutException)
            {
                Logger.LogInformation("Connection failed (ssh timed out), server is probably not up yet");
                await Requeue(job, retries - 1, server);
                return;
            }

            var imageDownloadUrl =
                remoteDownloadUrls.CreateDownloadFor(version.StorageFile, AppInfo.RemoteStorageDownloadExpireTime);

            // Connection success, so now we can run the job starting on the server
            job.RunningOnServerId = serverId;
            job.State = CIJobState.Running;

            // This save is done here as the build status might get reported back to us before we finish with the ssh
            // commands
            await Database.SaveChangesAsync(cancellationToken);

            // First is to download the CI executor script
            // TODO: is there a possibility that this is not secure? Someone would need to do HTTPS MItM attack...

            // TODO: using async would be nice for the run commands when supported

            // TODO: implement only re-downloading the CIExecutor if the hash has changed
            var result1 = sshAccess
                .RunCommand($"curl -L {GetUrlToDownloadCIExecutor()} -o ~/CIExecutor && chmod +x ~/CIExecutor");

            if (!result1.Success)
            {
                throw new Exception($"Failed to run executor download step: {result1.Result}, error: {result1.Error}");
            }

            // TODO: check server remaining disk space here

            // and then run it with environment variables for this build

            var env = new StringBuilder(250);
            env.Append("export CI_REF='");
            env.Append(EscapeForBash(job.Build.RemoteRef));
            env.Append("'; export CI_COMMIT_HASH='");
            env.Append(EscapeForBash(job.Build.CommitHash));
            env.Append("'; export CI_EARLIER_COMMIT='");
            env.Append(EscapeForBash(job.Build.PreviousCommit));
            env.Append("'; export CI_BRANCH='");
            env.Append(EscapeForBash(job.Build.Branch));
            env.Append("'; export CI_TRUSTED='");
            env.Append(job.Build.IsSafe);
            env.Append("'; export CI_ORIGIN='");
            env.Append(EscapeForBash(job.Build.CiProject.RepositoryCloneUrl));
            env.Append("'; export CI_IMAGE_DL_URL='");
            env.Append(EscapeForBash(imageDownloadUrl));
            env.Append("'; export CI_IMAGE_NAME='");
            env.Append(EscapeForBash(job.Image));
            env.Append("'; export CI_IMAGE_FILENAME='");
            env.Append(EscapeForBash(imageFileName));
            env.Append("'; export CI_CACHE_OPTIONS='");
            env.Append(EscapeForBash(job.CacheSettingsJson));
            env.Append("'; export CI_JOB_NAME='");
            env.Append(EscapeForBash(job.JobName));
            env.Append("';");

            var result2 = sshAccess.RunCommand($"{env} ~/CIExecutor {GetConnectToUrl(job)}");

            if (!result2.Success)
            {
                throw new Exception($"Failed to start running CI executor: {result2.Result}, error: {result2.Error}");
            }

            JobClient.Schedule<CheckCIJobOutputHasConnectedJob>(
                x => x.Execute(ciProjectId, ciBuildId, ciJobId, serverId, CancellationToken.None),
                TimeSpan.FromMinutes(5));

            JobClient.Schedule<CancelCIBuildIfStuckJob>(
                x => x.Execute(ciProjectId, ciBuildId, ciJobId, serverId, CancellationToken.None),
                TimeSpan.FromMinutes(61));

            Logger.LogInformation(
                "CI job startup succeeded, now it's up to the executor to contact us with updates");
        }

        private static string EscapeForBash(string commandPart)
        {
            if (string.IsNullOrEmpty(commandPart))
                return string.Empty;

            return commandPart.Replace(@"\", @"\\").Replace(@"'", @"\'");

            // return commandPart.Replace(@"\", @"\\").Replace(@"""", @"\""")
            //    .Replace(@"'", @"\'");
        }

        private async Task Requeue(CiJob job, int retries, ControlledServer server)
        {
            if (retries < 1)
            {
                Logger.LogError("CI build ran out of tries to try starting on the server");
                job.State = CIJobState.Finished;
                job.FinishedAt = DateTime.UtcNow;
                job.Succeeded = false;

                // TODO: add job output about the connect failure

                // Stop hogging the server and mark the build as failed
                await OnJobEnded(server, job);
                return;
            }

            JobClient.Schedule<RunJobOnServerJob>(x =>
                    x.Execute(job.CiProjectId, job.CiBuildId, job.CiJobId, server.Id, retries, CancellationToken.None),
                TimeSpan.FromSeconds(10));
        }

        private string GetUrlToDownloadCIExecutor()
        {
            return new Uri(configuration.GetBaseUrl(), "/CIExecutor").ToString();
        }

        private string GetConnectToUrl(CiJob job)
        {
            return new Uri(configuration.GetBaseUrl(), $"/ciBuildConnection?key={job.BuildOutputConnectKey}")
                .ToString();
        }
    }
}
