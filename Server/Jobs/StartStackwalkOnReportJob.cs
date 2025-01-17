namespace RevolutionaryWebApp.Server.Jobs;

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

/// <summary>
///   Performs stackwalking with a local stackwalk service. This has concurrent execution disabled as that seems like
///   the easiest way to make sure that these tasks can't take up a lot of processing power from other potential tasks.
/// </summary>
[DisableConcurrentExecution(1000)]
public class StartStackwalkOnReportJob
{
    private readonly ILogger<StartStackwalkOnReportJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IUploadFileStorage uploadFileStorage;
    private readonly IStackwalk stackwalk;
    private readonly IBackgroundJobClient jobClient;
    private readonly IStackwalkSymbolPreparer symbolPreparer;
    private readonly string symbolFolder;

    public StartStackwalkOnReportJob(ILogger<StartStackwalkOnReportJob> logger, NotificationsEnabledDb database,
        IConfiguration configuration, IUploadFileStorage uploadFileStorage, IStackwalk stackwalk,
        IBackgroundJobClient jobClient, IStackwalkSymbolPreparer symbolPreparer)
    {
        this.logger = logger;
        this.database = database;
        this.uploadFileStorage = uploadFileStorage;
        this.stackwalk = stackwalk;
        this.jobClient = jobClient;
        this.symbolPreparer = symbolPreparer;
        symbolFolder = configuration["Crashes:StackwalkSymbolFolder"] ??
            throw new InvalidOperationException("Stackwalk symbol folder not configured");
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

        if (string.IsNullOrEmpty(report.UploadStoragePath))
        {
            logger.LogError("Can't stackwalk on report that no longer has dump: {ReportId}", reportId);
            return;
        }

        // throw new NotImplementedException("Reimplement stackwalking with remotely stored files");

        var symbolPrepareTask = symbolPreparer.PrepareSymbolsInFolder(symbolFolder, cancellationToken);

        logger.LogInformation("Starting stackwalk on report {ReportId}", reportId);

        // TODO: the following is untested
        Stream dataContent;
        try
        {
            dataContent = await uploadFileStorage.GetObjectContent(report.UploadStoragePath);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to get dump file for report {ReportId}", reportId);
            throw;
        }
        finally
        {
            await symbolPrepareTask;
        }

        if (dataContent.Length < 0)
        {
            logger.LogError("Can't stackwalk on report with missing dump file: {ReportId}", reportId);
            return;
        }

        var startTime = DateTime.UtcNow;

        // TODO: implement an async API in the stackwalk service and swap to using that here
        var result = await stackwalk.PerformBlockingStackwalk(dataContent, report.Platform, cancellationToken);
        var primaryCallstack = stackwalk.FindPrimaryCallstack(result);
        var condensedCallstack = stackwalk.CondenseCallstack(primaryCallstack);

        cancellationToken.ThrowIfCancellationRequested();

        var duration = DateTime.UtcNow - startTime;

        logger.LogInformation("Stackwalking took: {Duration}", duration);

        await database.LogEntries.AddAsync(new LogEntry(
            $"Stackwalking performed on report {report.Id}, result length: {result.Length}, " +
            $"duration: {duration}"), cancellationToken);

        if (string.IsNullOrWhiteSpace(result))
            result = "Resulting decoded crash dump is empty";

        report.UpdateProcessedDumpIfChanged(result, primaryCallstack, condensedCallstack);

        await database.SaveChangesWithConflictResolvingAsync(conflictEntries =>
        {
            DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflictEntries, report);
            report.UpdateProcessedDumpIfChanged(result, primaryCallstack, condensedCallstack);
        }, cancellationToken);

        jobClient.Schedule<CheckCrashReportDuplicatesJob>(x => x.Execute(report.Id, CancellationToken.None),
            TimeSpan.FromSeconds(10));
    }
}
