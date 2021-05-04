namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Jobs;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    public class RemoteServerHandler
    {
        private readonly ILogger<RemoteServerHandler> logger;
        private readonly NotificationsEnabledDb database;
        private readonly EC2Controller ec2Controller;
        private readonly IBackgroundJobClient jobClient;
        private readonly Lazy<Task<List<ControlledServer>>> servers;

        private readonly int shutdownIdleDelay;
        private readonly int maximumRunningServers;
        private readonly TimeSpan terminateStoppedServerDelay = TimeSpan.FromDays(7);
        private readonly TimeSpan serverMaintenanceInterval = TimeSpan.FromDays(90);

        public RemoteServerHandler(ILogger<RemoteServerHandler> logger, IConfiguration configuration,
            NotificationsEnabledDb database, EC2Controller ec2Controller, IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.ec2Controller = ec2Controller;
            this.jobClient = jobClient;
            shutdownIdleDelay = Convert.ToInt32(configuration["CI:ServerIdleTimeBeforeStop"]);
            maximumRunningServers = Convert.ToInt32(configuration["CI:MaximumConcurrentServers"]);

            servers =
                new Lazy<Task<List<ControlledServer>>>(() =>
                    EntityFrameworkQueryableExtensions.ToListAsync(database.ControlledServers));
        }

        public bool NewServersAdded { get; private set; }

        public Task<List<ControlledServer>> GetServers()
        {
            return servers.Value;
        }

        public async Task CheckServerStatuses(CancellationToken cancellationToken)
        {
            var toCheck = new List<ControlledServer>();

            var now = DateTime.UtcNow;

            foreach (var server in await GetServers())
            {
                switch (server.Status)
                {
                    case ServerStatus.WaitingForStartup:
                    case ServerStatus.Stopping:
                    {
                        // Don't check servers too often
                        if (now - server.StatusLastChecked > TimeSpan.FromSeconds(5))
                            toCheck.Add(server);

                        break;
                    }
                }
            }

            if (toCheck.Count > 0)
            {
                foreach (var status in await ec2Controller.GetInstanceStatuses(
                    toCheck.AsEnumerable().Select(i => i.InstanceId).ToList(), cancellationToken))
                {
                    var actualStatus = EC2Controller.InstanceStateToStatus(status);

                    var match = toCheck.First(i => i.InstanceId == status.InstanceId);
                    match.StatusLastChecked = now;

                    if (match.Status != actualStatus)
                    {
                        logger.LogInformation("Server {Id} is now in status: {ActualStatus}", match.Id, actualStatus);
                        match.Status = actualStatus;
                        match.BumpUpdatedAt();

                        if (actualStatus == ServerStatus.Running)
                        {
                            match.PublicAddress = EC2Controller.InstanceIP(status);
                            match.RunningSince = now;
                        }
                        else if (match.RunningSince != null)
                        {
                            match.TotalRuntime += (now - match.RunningSince.Value).TotalSeconds;
                        }
                    }
                }
            }
        }

        public async Task<bool> HandleCIJobs(List<CiJob> ciJobsNeedingActions)
        {
            int missingServer = 0;

            var potentialServers = await GetServers();

            bool jobsNotRunning = false;

            foreach (var job in ciJobsNeedingActions)
            {
                if (job.State == CIJobState.Starting || job.State == CIJobState.WaitingForServer)
                {
                    // Need to find a server to run this job on
                    bool found = false;

                    foreach (var server in potentialServers)
                    {
                        if (server.Status == ServerStatus.Running && server.ProvisionedFully &&
                            !server.WantsMaintenance && server.ReservationType == ServerReservationType.None)
                        {
                            server.ReservationType = ServerReservationType.CIJob;
                            server.ReservedFor = job.CiJobId;
                            server.BumpUpdatedAt();

                            // Can run this job here
                            jobClient.Enqueue<RunJobOnServerJob>(x =>
                                x.Execute(job.CiProjectId, job.CiBuildId, job.CiJobId, server.Id,
                                    CancellationToken.None));
                            found = true;
                        }
                    }

                    if (!found)
                    {
                        ++missingServer;
                        jobsNotRunning = true;
                    }
                }
            }

            // Starting and provisioning servers reduce the count of missing servers
            missingServer -= potentialServers.Count(s =>
                s.Status == ServerStatus.Provisioning || s.Status == ServerStatus.WaitingForStartup);

            // Start some existing servers
            while (missingServer > 0)
            {
                bool foundAServer = false;

                foreach (var server in potentialServers)
                {
                    if (server.WantsMaintenance)
                        continue;

                    if (server.Status == ServerStatus.Stopped)
                    {
                        logger.LogInformation("Starting a stopped server to meet demand");
                        --missingServer;
                        foundAServer = true;

                        await ec2Controller.ResumeInstance(server.InstanceId);
                    }
                    else if (server.Status == ServerStatus.Terminated)
                    {
                        logger.LogInformation("Re-provisioning a terminated server to meet demand");
                        --missingServer;
                        foundAServer = true;

                        // This shouldn't create multiple at once, but the API returns a list
                        var awsServers = await ec2Controller.LaunchNewInstance();
                        NewServersAdded = true;
                        bool first = true;

                        foreach (var awsServer in awsServers)
                        {
                            if (!first)
                            {
                                logger.LogError(
                                    "AWS API created more servers than we wanted, attempting to terminate the extra");
                                await ec2Controller.TerminateInstance(awsServer);
                                throw new Exception("AWS API created more servers than we wanted");
                            }

                            first = false;

                            server.InstanceId = awsServer;
                            server.ProvisionedFully = false;
                            server.Status = ServerStatus.Provisioning;
                            server.LastMaintenance = DateTime.UtcNow;
                            server.StatusLastChecked = DateTime.UtcNow;

                            await database.SaveChangesAsync();

                            logger.LogInformation("Starting re-provisioning on {Id}", server.Id);

                            jobClient.Enqueue<ProvisionControlledServerJob>(x =>
                                x.Execute(server.Id, CancellationToken.None));
                        }
                    }
                }

                if (!foundAServer)
                    break;
            }

            // If still not enough, create new servers if allowed
            int startedNewServers = 0;
            while (missingServer > 0 && potentialServers.Count + startedNewServers < maximumRunningServers)
            {
                logger.LogInformation("Creating a new server to meet demand");

                // This shouldn't create multiple at once, but the API returns a list
                var awsServers = await ec2Controller.LaunchNewInstance();
                NewServersAdded = true;

                foreach (var awsServer in awsServers)
                {
                    var server = new ControlledServer()
                    {
                        InstanceId = awsServer
                    };

                    ++startedNewServers;
                    --missingServer;

                    await database.ControlledServers.AddAsync(server);
                    await database.SaveChangesAsync();

                    logger.LogInformation("New server {Id} created", server.Id);

                    jobClient.Enqueue<ProvisionControlledServerJob>(x => x.Execute(server.Id, CancellationToken.None));
                }
            }

            return jobsNotRunning;
        }

        public async Task ShutdownIdleServers()
        {
            var now = DateTime.UtcNow;

            foreach (var server in await GetServers())
            {
                if (server.ProvisionedFully && server.Status == ServerStatus.Running &&
                    server.ReservationType == ServerReservationType.None)
                {
                    // Can potentially time out if last modified a while ago
                    var idleTime = now - server.UpdatedAt;
                    if (idleTime > TimeSpan.FromSeconds(shutdownIdleDelay))
                    {
                        logger.LogInformation("Hibernating server {Id} because it's been idle for: {IdleTime}",
                            server.Id, idleTime);

                        await ec2Controller.StopInstance(server.InstanceId);

                        server.Status = ServerStatus.Stopping;
                        server.BumpUpdatedAt();
                    }
                }
            }

            // TODO: check server termination for servers that have been stopped for a while, or ones that
            // need maintenance
        }
    }
}
