namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Models;

    /// <summary>
    ///   Detects if some server has taken more than 2 hours to change state, and if so force-terminates it
    /// </summary>
    public class DetectStuckServersJob : IJob
    {
        private readonly ILogger<DetectStuckServersJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IEC2Controller ec2Controller;

        public DetectStuckServersJob(ILogger<DetectStuckServersJob> logger, NotificationsEnabledDb database,
            IEC2Controller ec2Controller)
        {
            this.logger = logger;
            this.database = database;
            this.ec2Controller = ec2Controller;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            if (!ec2Controller.Configured)
                return;

            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(2);

            foreach (var server in await database.ControlledServers.AsQueryable().Where(s =>
                    s.UpdatedAt < cutoff && s.Status != ServerStatus.Stopped && s.Status != ServerStatus.Terminated)
                .ToListAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogError(
                    "Server {Id} is stuck! Last state change: {UpdatedAt} current state: {Status}, terminating it",
                    server.Id, server.UpdatedAt, server.Status);

                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message =
                        $"Server {server.Id} ({server.InstanceId}) is stuck in state {server.Status} " +
                        $"since {server.UpdatedAt}"
                }, cancellationToken);

                await ec2Controller.TerminateInstance(server.InstanceId);

                server.Status = ServerStatus.Terminated;

                if (server.ReservationType != ServerReservationType.None)
                {
                    logger.LogWarning("Stuck server was reserved for type: {ReservationType}", server.ReservationType);
                    server.ReservationType = ServerReservationType.None;
                }

                if (server.RunningSince != null)
                    server.TotalRuntime += (DateTime.UtcNow - server.RunningSince.Value).TotalSeconds;
                server.RunningSince = null;

                logger.LogInformation("Successfully terminated: {InstanceId}", server.InstanceId);

                // Not cancellable done as the state to terminated is very important to save
                // ReSharper disable once MethodSupportsCancellation
                await database.SaveChangesAsync();
            }
        }
    }
}
