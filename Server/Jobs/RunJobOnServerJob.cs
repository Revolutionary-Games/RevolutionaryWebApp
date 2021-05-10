namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Renci.SshNet.Common;
    using Services;
    using Shared.Models;
    using Utilities;

    public class RunJobOnServerJob
    {
        public const int DefaultJobConnectRetries = 30;

        private readonly ILogger<RunJobOnServerJob> logger;
        private readonly IConfiguration configuration;
        private readonly NotificationsEnabledDb database;
        private readonly ControlledServerSSHAccess sshAccess;
        private readonly IBackgroundJobClient jobClient;
        private readonly GithubCommitStatusReporter statusReporter;

        public RunJobOnServerJob(ILogger<RunJobOnServerJob> logger, IConfiguration configuration,
            NotificationsEnabledDb database, ControlledServerSSHAccess sshAccess, IBackgroundJobClient jobClient,
            GithubCommitStatusReporter statusReporter)
        {
            this.logger = logger;
            this.configuration = configuration;
            this.database = database;
            this.sshAccess = sshAccess;
            this.jobClient = jobClient;
            this.statusReporter = statusReporter;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, long ciJobId, long serverId, int retries,
            CancellationToken cancellationToken)
        {
            // Includes are needed here to provide fully populated data for update notifications
            var job = await database.CiJobs.Include(j => j.Build).ThenInclude(b => b.CiProject)
                .FirstOrDefaultAsync(
                    j => j.CiProjectId == ciProjectId && j.CiBuildId == ciBuildId && j.CiJobId == ciJobId,
                    cancellationToken);
            var server = await database.ControlledServers.FindAsync(new object[] { serverId }, cancellationToken);

            if (server == null)
                throw new ArgumentException("Could not find server to run build on");

            if (job == null)
            {
                ReleaseServerReservation(server);
                logger.LogWarning("Skipping CI job as it doesn't exist");
                return;
            }

            // TODO: check if ciJobId matches the reservation on the server?

            logger.LogInformation("Trying to start job {CIProjectId}-{CIBuildId}-{CIJobId} on reserved server",
                ciProjectId, ciBuildId, ciJobId);

            // Try to start running the job, this can fail if the server is not actually really up yet
            try
            {
                sshAccess.ConnectTo(server.PublicAddress.ToString());
            }
            catch (SocketException)
            {
                logger.LogWarning("Connection failed (socket exception), server is probably not up yet");
                await Requeue(job, retries - 1, server);
                return;
            }
            catch (SshOperationTimeoutException)
            {
                logger.LogWarning("Connection failed (ssh timed out), server is probably not up yet");
                await Requeue(job, retries - 1, server);
                return;
            }

            // Connection success, so now we can run the job starting on the server

            // First is to download the CI executor script
            // TODO: is there a possibility that this is not secure? Someone would need to do HTTPS MItM attack...

            // TODO: using async would be nice for the run commands when supported
            var output1 = sshAccess
                .RunCommand($"curl -L {GetUrlToDownloadCIExecutor()} -o ~/executor.rb && chmod +x ~/executor.rb")
                .Result;

            // and then run it with environment variables for this build

            // TODO: build image name, DL urls, keys env variables
            var env = $"export CI_REF={EscapeForBash(job.Build.RemoteRef)};";

            // TODO: implement the log and final status getting through a different endpoint, for now pretend that things succeeded
            var output2 = sshAccess.RunCommand($"{env} ~/executor.rb {GetConnectToUrl(job)}").Result;

            logger.LogInformation("Command results: {Output1}\n{Output2}", output1, output2);

            logger.LogInformation("Pretending that job is complete");

            job.State = CIJobState.Finished;
            job.FinishedAt = DateTime.UtcNow;
            job.Succeeded = true;

            await OnJobEnded(server, job);
        }

        private async Task OnJobEnded(ControlledServer server, CiJob job)
        {
            ReleaseServerReservation(server);

            // After running the job, the changes saving should not be skipped
            await database.SaveChangesAsync();

            // Send status to github
            var status = GithubAPI.CommitStatus.Success;
            string statusDescription = "Checks succeeded";

            if (!job.Succeeded)
            {
                status = GithubAPI.CommitStatus.Failure;
                statusDescription = "Some checks failed";
            }

            if (!await statusReporter.SetCommitStatus(job.Build.CiProject.RepositoryFullName, job.Build.CommitHash,
                status, statusReporter.CreateStatusUrlForJob(job), statusDescription,
                job.JobName))
            {
                logger.LogError("Failed to set commit status for build's job: {JobName}", job.JobName);
            }

            jobClient.Enqueue<CheckOverallBuildStatusJob>(x =>
                x.Execute(job.CiProjectId, job.CiBuildId, CancellationToken.None));
        }

        private static string EscapeForBash(string commandPart)
        {
            return commandPart.Replace(@"\", @"\\").Replace(@"""", @"\""")
                .Replace(@"'", @"\'");
        }

        private void ReleaseServerReservation(ControlledServer server)
        {
            logger.LogInformation("Releasing reservation on server {Id}", server.Id);
            server.ReservationType = ServerReservationType.None;
            server.ReservedFor = null;
            server.BumpUpdatedAt();
        }

        private async Task Requeue(CiJob job, int retries, ControlledServer server)
        {
            if (retries < 1)
            {
                logger.LogError("CI build ran out of tries to try starting on the server");
                job.State = CIJobState.Finished;
                job.FinishedAt = DateTime.UtcNow;
                job.Succeeded = false;

                // TODO: add job output about the connect failure

                // Stop hogging the server and mark the build as failed
                await OnJobEnded(server, job);
                return;
            }

            jobClient.Schedule<RunJobOnServerJob>(x =>
                    x.Execute(job.CiProjectId, job.CiBuildId, job.CiJobId, server.Id, retries, CancellationToken.None),
                TimeSpan.FromSeconds(10));
        }

        private string GetUrlToDownloadCIExecutor()
        {
            return new Uri(configuration.GetBaseUrl(), "/ci_executor.rb").ToString();
        }

        private string GetConnectToUrl(CiJob job)
        {
            return new Uri(configuration.GetBaseUrl(), $"/ciBuildConnection?jobId={job.CiJobId}").ToString();
        }
    }
}
