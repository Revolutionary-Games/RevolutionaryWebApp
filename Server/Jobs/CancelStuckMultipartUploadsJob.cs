namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;

public class CancelStuckMultipartUploadsJob : IJob
{
    private readonly ILogger<CancelStuckMultipartUploadsJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IGeneralRemoteStorage remoteStorage;
    private readonly IBackgroundJobClient jobClient;

    public CancelStuckMultipartUploadsJob(ILogger<CancelStuckMultipartUploadsJob> logger,
        ApplicationDbContext database, IGeneralRemoteStorage remoteStorage, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.remoteStorage = remoteStorage;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        await DetectOldNonFinishedRecords(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        if (!remoteStorage.Configured)
        {
            logger.LogInformation(
                "Skipping trying to detect multipart uploads we don't know of as remote storage " +
                "is not configured");
            return;
        }

        await QueryAndCancelMultipartUploadsFromRemoteStorage(cancellationToken);
    }

    /// <summary>
    ///   There may be old DB records that are not cleared out. This looks for them and queues a job to remove them
    /// </summary>
    /// <param name="cancellationToken">Cancellation</param>
    private async Task DetectOldNonFinishedRecords(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.OldMultipartUploadThreshold;

        var items = await database.InProgressMultipartUploads.Where(i => i.UpdatedAt < cutoff && !i.Finished)
            .ToListAsync(cancellationToken);

        if (items.Count < 1)
            return;

        cancellationToken.ThrowIfCancellationRequested();

        foreach (var item in items)
        {
            logger.LogError(
                "Found old multipart upload data ({Id}) that has not finished or canceled, queueing a job to abort it",
                item.Id);

            jobClient.Schedule<DeleteNonFinishedMultipartUploadJob>(
                x => x.Execute(item.UploadId, CancellationToken.None),
                AppInfo.MultipartUploadTotalAllowedTime + TimeSpan.FromHours(2));
        }
    }

    private async Task QueryAndCancelMultipartUploadsFromRemoteStorage(CancellationToken cancellationToken)
    {
        var activeUploads = await database.InProgressMultipartUploads.Where(i => !i.Finished)
            .ToListAsync(cancellationToken);

        var uploads = await remoteStorage.ListMultipartUploads(cancellationToken);

        // Find uploads that don't match any active ones, those we want to cancel
        foreach (var upload in uploads)
        {
            bool match = false;

            foreach (var activeUpload in activeUploads)
            {
                if (activeUpload.Path == upload.Key && activeUpload.UploadId == upload.UploadId)
                {
                    match = true;
                    break;
                }
            }

            if (match)
                continue;

            logger.LogError("Detected multipart upload that we have no record of {UploadId} for path: {Key}"
                + " will attempt to terminate it",
                upload.UploadId, upload.Key);

            await remoteStorage.AbortMultipartUpload(upload.Key, upload.UploadId);

            jobClient.Schedule<MakeSureNoMultipartPartsExistJob>(
                x => x.Execute(upload.UploadId, upload.Key, CancellationToken.None),
                TimeSpan.FromDays(1));
        }
    }
}
