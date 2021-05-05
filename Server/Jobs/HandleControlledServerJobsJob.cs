namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Models;

    [DisableConcurrentExecution(60)]
    public class HandleControlledServerJobsJob : IJob
    {
        private readonly ILogger<HandleControlledServerJobsJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly RemoteServerHandler serverHandler;
        private readonly IBackgroundJobClient jobClient;

        public HandleControlledServerJobsJob(ILogger<HandleControlledServerJobsJob> logger,
            NotificationsEnabledDb database, RemoteServerHandler serverHandler,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.serverHandler = serverHandler;
            this.jobClient = jobClient;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            var ciJobsNeedingActions = await database.CiJobs.AsQueryable().Where(j => j.State != CIJobState.Finished)
                .ToListAsync(cancellationToken);

            await serverHandler.CheckServerStatuses(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            bool queuedRecheck = false;

            // Cancellation tokens are not used from here on out to avoid not saving changes
            if (!await serverHandler.HandleCIJobs(ciJobsNeedingActions))
            {
                // Some job could not start running, try again in 5 seconds
                logger.LogInformation(
                    "One or more jobs could not start executing immediately, trying again in 10 seconds");
                queuedRecheck = true;
                jobClient.Schedule<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None),
                    TimeSpan.FromSeconds(10));
            }

            // Skip this if we should cancel
            if(!cancellationToken.IsCancellationRequested)
                await serverHandler.ShutdownIdleServers();

            // ReSharper disable once MethodSupportsCancellation
            await database.SaveChangesAsync();

            // If we have active servers, queue a check in 1 minute
            if (!queuedRecheck && (serverHandler.NewServersAdded || (await serverHandler.GetServers()).Any(s =>
                s.Status == ServerStatus.Provisioning || s.Status == ServerStatus.Running ||
                s.Status == ServerStatus.Stopping || s.Status == ServerStatus.WaitingForStartup)))
            {
                jobClient.Schedule<HandleControlledServerJobsJob>(x => x.Execute(CancellationToken.None),
                    TimeSpan.FromSeconds(60));
            }
        }
    }
}
