namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Shared.Models.Enums;
    using Utilities;

    [DisableConcurrentExecution(300)]
    public class CheckCrashReportDuplicatesJob
    {
        private readonly ILogger<CheckCrashReportDuplicatesJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly DiscordNotifications discordNotifications;
        private readonly IBackgroundJobClient jobClient;
        private readonly Uri baseUrl;

        public CheckCrashReportDuplicatesJob(ILogger<CheckCrashReportDuplicatesJob> logger,
            NotificationsEnabledDb database, IConfiguration configuration, DiscordNotifications discordNotifications,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.discordNotifications = discordNotifications;
            this.jobClient = jobClient;

            baseUrl = configuration.GetBaseUrl();
        }

        public async Task Execute(long reportId, CancellationToken cancellationToken)
        {
            var report = await database.CrashReports.FindAsync(new object[] { reportId }, cancellationToken);

            if (report == null)
            {
                logger.LogWarning("Can't auto check report if it is a duplicate as it doesn't exist: {ReportId}",
                    reportId);
                return;
            }

            // Only open reports can be considered duplicates
            if (report.State != ReportState.Open)
            {
                return;
            }

            if (report.CondensedCallstack == null)
            {
                logger.LogWarning(
                    "Report is missing primary (condensed) callstack, can't check whether it is a " +
                    "duplicate automatically: {ReportId}",
                    reportId);
                return;
            }

            // TODO: should this use the first 75% of the stack lines to find duplicates (but at least 3)?

            var potentiallyDuplicateOf = await database.CrashReports.AsQueryable().Where(r =>
                    r.CondensedCallstack != null && r.Id != report.Id && r.State != ReportState.Duplicate &&
                    r.CondensedCallstack.Contains(report.CondensedCallstack))
                .OrderBy(r => r.Id).FirstOrDefaultAsync(cancellationToken);

            if (potentiallyDuplicateOf == null)
            {
                logger.LogInformation("Report {ReportId} is not a duplicate", reportId);
                return;
            }

            report.DuplicateOfId = potentiallyDuplicateOf.Id;
            report.State = ReportState.Duplicate;

            await database.SaveChangesWithConflictResolvingAsync(
                conflictEntries =>
                {
                    DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflictEntries, report);
                    report.DuplicateOfId = potentiallyDuplicateOf.Id;

                    if (report.State != ReportState.Closed)
                        report.State = ReportState.Duplicate;
                }, cancellationToken);

            logger.LogInformation("Report {ReportId} seems to be a duplicate of {Id}", reportId,
                potentiallyDuplicateOf.Id);

            jobClient.Enqueue<SendCrashReportWatcherNotificationsJob>(x => x.Execute(reportId,
                $"Report was detected to be a duplicate of #{potentiallyDuplicateOf.Id} automatically",
                CancellationToken.None));

            discordNotifications.NotifyCrashReportStateChanged(report, baseUrl);
        }
    }
}
