namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared.Models;

    public class ScheduleServerMaintenanceJob : IJob
    {
        private readonly TimeSpan serverMaintenanceInterval = TimeSpan.FromDays(30);

        private readonly ILogger<ScheduleServerMaintenanceJob> logger;
        private readonly ApplicationDbContext database;

        public ScheduleServerMaintenanceJob(ILogger<ScheduleServerMaintenanceJob> logger,
            ApplicationDbContext database)
        {
            this.logger = logger;
            this.database = database;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow - serverMaintenanceInterval;

            // Only one server is scheduled for maintenance at once to avoid all being unavailable for job running
            var serverToMaintain = await database.ControlledServers.AsQueryable()
                .Where(s => !s.WantsMaintenance && s.Status != ServerStatus.Terminated && s.LastMaintenance < cutoff)
                .OrderBy(s => s.LastMaintenance)
                .FirstOrDefaultAsync(cancellationToken);

            if (serverToMaintain == null)
                return;

            serverToMaintain.WantsMaintenance = true;
            await database.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Scheduled server {Id} for maintenance", serverToMaintain.Id);
        }
    }
}
