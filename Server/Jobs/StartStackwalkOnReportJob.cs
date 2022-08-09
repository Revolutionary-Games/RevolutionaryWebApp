namespace ThriveDevCenter.Server.Jobs;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Configuration;
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
    private readonly IStackwalkSymbolPreparer symbolPreparer;
    private readonly string symbolFolder;

    public StartStackwalkOnReportJob(ILogger<StartStackwalkOnReportJob> logger, NotificationsEnabledDb database,
        IConfiguration configuration, ILocalTempFileLocks localTempFileLocks, IStackwalk stackwalk,
        IBackgroundJobClient jobClient, IStackwalkSymbolPreparer symbolPreparer)
    {
        this.logger = logger;
        this.database = database;
        this.localTempFileLocks = localTempFileLocks;
        this.stackwalk = stackwalk;
        this.jobClient = jobClient;
        this.symbolPreparer = symbolPreparer;
        symbolFolder = configuration["Crashes:StackwalkSymbolFolder"];
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

        if (string.IsNullOrEmpty(report.DumpLocalFileName))
        {
            logger.LogError("Can't stackwalk on report that no longer has local dump: {ReportId}", reportId);
            return;
        }

        var symbolPrepareTask = symbolPreparer.PrepareSymbolsInFolder(symbolFolder, cancellationToken);

        logger.LogInformation("Starting stackwalk on report {ReportId}", reportId);

        var semaphore =
            localTempFileLocks.GetTempFilePath(CrashReport.CrashReportTempStorageFolderName, out string baseFolder);

        var filePath = Path.Combine(baseFolder, report.DumpLocalFileName);

        FileStream? dump = null;

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

        await symbolPrepareTask;

        if (report.DumpLocalFileName == null || dump == null)
        {
            logger.LogError("Can't stackwalk on report with missing dump file: {ReportId}", reportId);
            return;
        }

        var startTime = DateTime.UtcNow;

        // TODO: implement an async API in the stackwalk service and swap to using that here
        var result = await stackwalk.PerformBlockingStackwalk(dump, report.Platform, cancellationToken);
        var primaryCallstack = stackwalk.FindPrimaryCallstack(result);
        var condensedCallstack = stackwalk.CondenseCallstack(primaryCallstack);

        cancellationToken.ThrowIfCancellationRequested();

        var duration = DateTime.UtcNow - startTime;

        logger.LogInformation("Stackwalking took: {Duration}", duration);

        await database.LogEntries.AddAsync(new LogEntry
        {
            Message = $"Stackwalking performed on report {report.Id}, result length: {result.Length}, " +
                $"duration: {duration}",
        }, cancellationToken);

        if (string.IsNullOrWhiteSpace(result))
            result = "Resulting decoded crash dump is empty";

        report.UpdateProcessedDumpIfChanged(result, primaryCallstack, condensedCallstack);

        await database.SaveChangesWithConflictResolvingAsync(
            conflictEntries =>
            {
                DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflictEntries, report);
                report.UpdateProcessedDumpIfChanged(result, primaryCallstack, condensedCallstack);
            }, cancellationToken);

        jobClient.Schedule<CheckCrashReportDuplicatesJob>(x => x.Execute(report.Id, CancellationToken.None),
            TimeSpan.FromSeconds(10));
    }
}