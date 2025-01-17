namespace RevolutionaryWebApp.Server.Jobs;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;

[DisableConcurrentExecution(500)]
public class DeleteCrashReportJob
{
    private readonly ILogger<DeleteCrashReportJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IUploadFileStorage uploadFileStorage;

    public DeleteCrashReportJob(ILogger<DeleteCrashReportJob> logger, NotificationsEnabledDb database,
        IUploadFileStorage uploadFileStorage)
    {
        this.logger = logger;
        this.database = database;
        this.uploadFileStorage = uploadFileStorage;
    }

    public async Task Execute(long reportId, CancellationToken cancellationToken)
    {
        var report = await database.CrashReports.FindAsync(new object[] { reportId }, cancellationToken);

        if (report == null)
        {
            logger.LogWarning("Can't delete crash report that doesn't exist: {ReportId}", reportId);
            return;
        }

        // Delete the data that depends on the report first

        // Any reports that are a duplicate of this need to be modified
        var duplicates = await database.CrashReports.Where(r => r.DuplicateOfId == report.Id)
            .OrderBy(r => r.Id).ToListAsync(cancellationToken);

        if (duplicates.Count > 1)
        {
            // The first non-private-item will be the new primary report, and the other ones are the duplicates
            var newDuplicatePrimary = duplicates.FirstOrDefault(r => r.Public) ?? duplicates.First();

            newDuplicatePrimary.State = report.State;
            newDuplicatePrimary.DuplicateOfId = null;
            newDuplicatePrimary.BumpUpdatedAt();

            await database.LogEntries.AddAsync(new LogEntry(
                $"Crash report {newDuplicatePrimary.Id} has become primary report for a group " +
                $"of duplicates (size: {duplicates.Count}) due to report deletion"), cancellationToken);

            foreach (var duplicate in duplicates.Where(r => r != newDuplicatePrimary))
            {
                duplicate.DuplicateOfId = newDuplicatePrimary.Id;
                duplicate.BumpUpdatedAt();
            }
        }
        else if (duplicates.Count == 1)
        {
            var duplicate = duplicates[0];

            duplicate.State = report.State;
            duplicate.DuplicateOfId = null;
            duplicate.BumpUpdatedAt();

            await database.LogEntries.AddAsync(
                new LogEntry($"Crash report {duplicate.Id} has become non-duplicate due to report deletion"),
                cancellationToken);
        }

        // Dump file needs to be deleted
        if (report.UploadStoragePath != null)
        {
            if (await uploadFileStorage.DoesObjectExist(report.UploadStoragePath, cancellationToken))
                await uploadFileStorage.DeleteObject(report.UploadStoragePath);
        }

        // Cancellation tokens are used even after that local file delete as it is not an error if the file is
        // already deleted

        await database.LogEntries.AddAsync(new LogEntry($"Crash report {report.Id} is now deleted"), cancellationToken);

        database.CrashReports.Remove(report);
        await database.SaveChangesAsync(cancellationToken);
    }
}
