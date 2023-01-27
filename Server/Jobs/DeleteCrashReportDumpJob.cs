namespace ThriveDevCenter.Server.Jobs;

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Utilities;

public class DeleteCrashReportDumpJob
{
    private readonly ILogger<DeleteCrashReportDumpJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly ILocalTempFileLocks localTempFileLocks;

    public DeleteCrashReportDumpJob(ILogger<DeleteCrashReportDumpJob> logger, NotificationsEnabledDb database,
        ILocalTempFileLocks localTempFileLocks)
    {
        this.logger = logger;
        this.database = database;
        this.localTempFileLocks = localTempFileLocks;
    }

    public static async Task DeleteReportTempFile(CrashReport report, ILocalTempFileLocks fileLocks, ILogger logger,
        CancellationToken cancellationToken)
    {
        var baseFolder =
            fileLocks.GetTempFilePath(CrashReport.CrashReportTempStorageFolderName);

        if (string.IsNullOrEmpty(report.DumpLocalFileName))
        {
            logger.LogInformation("Crash report doesn't have a dump file set, skip deleting it");
            return;
        }

        var filePath = Path.Combine(baseFolder, report.DumpLocalFileName);

        using var baseFolderLock = await fileLocks.LockAsync(baseFolder, cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(baseFolder))
        {
            logger.LogInformation("Crash report dump folder doesn't exist, skip deleting a dump file");
            return;
        }

        if (!File.Exists(filePath))
        {
            logger.LogInformation(
                "Crash report dump file with name {DumpLocalFileName} doesn't exist, skip trying to delete it",
                report.DumpLocalFileName);
            return;
        }

        File.Delete(filePath);

        logger.LogInformation("Deleted crash dump file {DumpLocalFileName}", report.DumpLocalFileName);
    }

    public async Task Execute(long reportId, CancellationToken cancellationToken)
    {
        var report = await database.CrashReports.FindAsync(new object[] { reportId }, cancellationToken);

        if (report == null)
        {
            logger.LogWarning("Can't delete dump file for non-existent report: {ReportId}", reportId);
            return;
        }

        if (report.DumpLocalFileName == null)
        {
            logger.LogInformation("Crash report dump file is already deleted for report: {ReportId}", reportId);
            return;
        }

        await DeleteReportTempFile(report, localTempFileLocks, logger, cancellationToken);

        await database.LogEntries.AddAsync(new LogEntry
        {
            Message = $"Deleted crash dump file for {report.Id}",
        }, cancellationToken);

        report.DumpLocalFileName = null;
        report.BumpUpdatedAt();

        await database.SaveChangesWithConflictResolvingAsync(
            conflictEntries =>
            {
                DatabaseConcurrencyHelpers.ResolveSingleEntityConcurrencyConflict(conflictEntries, report);
                report.DumpLocalFileName = null;
                report.BumpUpdatedAt();
            }, cancellationToken);
    }
}
