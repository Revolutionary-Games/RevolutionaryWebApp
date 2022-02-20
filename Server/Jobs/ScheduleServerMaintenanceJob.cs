namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Models;
    using Shared.Models;

    /// <summary>
    ///   Schedules controlled servers for maintenance if they haven't been
    /// </summary>
    public class ScheduleServerMaintenanceJob : IJob
    {
        private readonly TimeSpan serverMaintenanceInterval = TimeSpan.FromDays(30);

        private readonly NotificationsEnabledDb database;

        public ScheduleServerMaintenanceJob(NotificationsEnabledDb database)
        {
            this.database = database;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow - serverMaintenanceInterval;

            await CheckControlledServers(cutoff, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            await CheckExternalServers(cutoff, cancellationToken);
        }

        private async Task CheckControlledServers(DateTime cutoff, CancellationToken cancellationToken)
        {
            // Only one server is scheduled for maintenance at once to avoid all being unavailable for job running
            var serverToMaintain = await database.ControlledServers.Where(s =>
                    !s.WantsMaintenance && s.Status != ServerStatus.Terminated && s.LastMaintenance < cutoff)
                .OrderBy(s => s.LastMaintenance).FirstOrDefaultAsync(cancellationToken);

            if (serverToMaintain == null)
                return;

            serverToMaintain.WantsMaintenance = true;

            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"Scheduled controlled server {serverToMaintain.Id} for termination due to maintenance",
            }, cancellationToken);

            await database.SaveChangesAsync(cancellationToken);
        }

        private async Task CheckExternalServers(DateTime cutoff, CancellationToken cancellationToken)
        {
            var serverToMaintain = await database.ExternalServers.Where(s =>
                    !s.WantsMaintenance && s.Status == ServerStatus.Running && s.LastMaintenance < cutoff)
                .OrderBy(s => s.LastMaintenance).FirstOrDefaultAsync(cancellationToken);

            if (serverToMaintain == null)
                return;

            serverToMaintain.WantsMaintenance = true;

            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"Scheduled external server {serverToMaintain.Id} for maintenance",
            }, cancellationToken);

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
