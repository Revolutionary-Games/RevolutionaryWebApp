namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Utilities;

    [DisableConcurrentExecution(1000)]
    public class StartStackwalkOnReportJob
    {
        private readonly ILogger<StartStackwalkOnReportJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly ILocalTempFileLocks localTempFileLocks;
        private readonly IStackwalk stackwalk;
        private readonly IBackgroundJobClient jobClient;

        public StartStackwalkOnReportJob(ILogger<StartStackwalkOnReportJob> logger, NotificationsEnabledDb database,
            ILocalTempFileLocks localTempFileLocks, IStackwalk stackwalk, IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.localTempFileLocks = localTempFileLocks;
            this.stackwalk = stackwalk;
            this.jobClient = jobClient;
        }

        public async Task Execute(long reportId, CancellationToken cancellationToken)
        {
            if (!stackwalk.Configured)
                throw new Exception("Stackwalk is not configured");

            var report = await database.CrashReports.FindAsync(new object[] { reportId }, cancellationToken);

            if (report == null)
            {
                logger.LogError("Can't stackwalk on non-existent report: {ReportId}", reportId);
                return;
            }

            logger.LogInformation("Starting stackwalk on report {ReportId}", reportId);

            var semaphore =
                localTempFileLocks.GetTempFilePath(CrashReport.CrashReportTempStorageFolderName, out string baseFolder);

            var filePath = Path.Combine(baseFolder, report.DumpLocalFileName);

            FileStream dump = null;

            // On Linux an open file should not impact deleting etc. so I'm pretty sure this is pretty safe
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                if (File.Exists(filePath))
                    dump = File.OpenRead(filePath);
            }
            finally
            {
                semaphore.Release();
            }

            if (report.DumpLocalFileName == null || dump == null)
            {
                logger.LogError("Can't stackwalk on report with missing dump file: {ReportId}", reportId);
                return;
            }

            // TODO: implement an async API in the stackwalk service and swap to using that here
            var result = await stackwalk.PerformBlockingStackwalk(dump, cancellationToken);
            var primaryCallstack = stackwalk.FindPrimaryCallstack(result);

            cancellationToken.ThrowIfCancellationRequested();

            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"Stackwalking performed on {report.Id}, result length: {result.Length}",
            }, cancellationToken);

            if (string.IsNullOrWhiteSpace(result))
                result = "Resulting decoded crash dump is empty";

            report.WholeCrashDump = result;
            report.PrimaryCallstack = primaryCallstack;

            await database.SaveChangesWithConflictResolvingAsync(
                conflictEntries =>
                {
                    DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflictEntries, report);
                    report.WholeCrashDump = result;
                    report.PrimaryCallstack = primaryCallstack;
                }, cancellationToken);

            jobClient.Schedule<CheckCrashReportDuplicatesJob>(x => x.Execute(report.Id, CancellationToken.None),
                TimeSpan.FromSeconds(10));
        }
    }
}
