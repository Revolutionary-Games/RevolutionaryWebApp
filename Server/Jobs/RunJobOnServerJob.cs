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

    public class RunJobOnServerJob : BaseCIJobManagingJob
    {
        public const int DefaultJobConnectRetries = 30;

        private readonly IConfiguration configuration;
        private readonly ControlledServerSSHAccess sshAccess;

        public RunJobOnServerJob(ILogger<RunJobOnServerJob> logger, IConfiguration configuration,
            NotificationsEnabledDb database, ControlledServerSSHAccess sshAccess, IBackgroundJobClient jobClient,
            GithubCommitStatusReporter statusReporter) : base(logger, database, jobClient, statusReporter)
        {
            this.configuration = configuration;
            this.sshAccess = sshAccess;
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

            Logger.LogInformation("Trying to start job {CIProjectId}-{CIBuildId}-{CIJobId} on reserved server",
                ciProjectId, ciBuildId, ciJobId);

            // Try to start running the job, this can fail if the server is not actually really up yet
            try
            {
                sshAccess.ConnectTo(server.PublicAddress.ToString());
            }
            catch (SocketException)
            {
                Logger.LogWarning("Connection failed (socket exception), server is probably not up yet");
                await Requeue(job, retries - 1, server);
                return;
            }
            catch (SshOperationTimeoutException)
            {
                Logger.LogWarning("Connection failed (ssh timed out), server is probably not up yet");
                await Requeue(job, retries - 1, server);
                return;
            }

            // Connection success, so now we can run the job starting on the server
            job.RunningOnServerId = serverId;
            job.State = CIJobState.Running;

            // First is to download the CI executor script
            // TODO: is there a possibility that this is not secure? Someone would need to do HTTPS MItM attack...

            // TODO: using async would be nice for the run commands when supported
            var result1 = sshAccess
                .RunCommand($"curl -L {GetUrlToDownloadCIExecutor()} -o ~/executor.rb && chmod +x ~/executor.rb");

            if (!result1.Success)
            {
                throw new Exception($"Failed to run executor download step: {result1.Result}, error: {result1.Error}");
            }

            // and then run it with environment variables for this build

            // TODO: build image name, DL urls, keys, and other env variables
            var env = $"export CI_REF={EscapeForBash(job.Build.RemoteRef)};";

            // TODO: implement the log and final status getting through a different endpoint, for now pretend that things succeeded
            var result2 = sshAccess.RunCommand($"{env} ~/executor.rb {GetConnectToUrl(job)}");

            if (!result2.Success)
            {
                throw new Exception($"Failed to start running CI executor: {result2.Result}, error: {result2.Error}");
            }

            // Don't want to cancel saving once the job is already running
            // ReSharper disable once MethodSupportsCancellation
            await Database.SaveChangesAsync();

            JobClient.Schedule<CheckCIJobOutputHasConnectedJob>(
                x => x.Execute(ciProjectId, ciBuildId, ciJobId, serverId, cancellationToken), TimeSpan.FromMinutes(5));

            JobClient.Schedule<CancelCIBuildIfStuckJob>(
                x => x.Execute(ciProjectId, ciBuildId, ciJobId, serverId, cancellationToken), TimeSpan.FromMinutes(61));

            Logger.LogInformation(
                "CI job startup succeeded, now it's up for the executor to contact us with updates");
        }

        private static string EscapeForBash(string commandPart)
        {
            return commandPart.Replace(@"\", @"\\").Replace(@"""", @"\""")
                .Replace(@"'", @"\'");
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
            return new Uri(configuration.GetBaseUrl(), "/ci_executor.rb").ToString();
        }

        private string GetConnectToUrl(CiJob job)
        {
            return new Uri(configuration.GetBaseUrl(), $"/ciBuildConnection?key={job.BuildOutputConnectKey}")
                .ToString();
        }
    }
}
