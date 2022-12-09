namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;

public class DeleteNonFinishedMultipartUploadJob
{
    private readonly ILogger<DeleteNonFinishedMultipartUploadJob> logger;
    private readonly ApplicationDbContext database;
    private readonly GeneralRemoteStorage remoteStorage;
    private readonly IBackgroundJobClient jobClient;

    public DeleteNonFinishedMultipartUploadJob(ILogger<DeleteNonFinishedMultipartUploadJob> logger,
        ApplicationDbContext database, GeneralRemoteStorage remoteStorage, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.remoteStorage = remoteStorage;
        this.jobClient = jobClient;
    }

    public async Task Execute(string uploadId, CancellationToken cancellationToken)
    {
        var upload = await database.InProgressMultipartUploads.FirstOrDefaultAsync(u => u.UploadId == uploadId,
            cancellationToken);

        if (upload == null)
        {
            logger.LogError("Failed to check if multipart upload is stuck, not found in db: {UploadId}", uploadId);
            return;
        }

        if (!upload.Finished)
        {
            // Seems stuck
            logger.LogWarning("Detected abandoned multipart upload ({Path}) {UploadId}, attempting to delete",
                upload.Path, upload.UploadId);

            try
            {
                await remoteStorage.AbortMultipartUpload(upload.Path, upload.UploadId);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to abort the multipart upload. Still going to try to cancel it later");
            }

            jobClient.Schedule<MakeSureNoMultipartPartsExistJob>(
                x => x.Execute(uploadId, upload.Path, CancellationToken.None),
                TimeSpan.FromDays(1));
        }

        // We can now remove this item as this is handled
        database.InProgressMultipartUploads.Remove(upload);

        // This is not cancellable as we have potentially made destructive changes already
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();
    }
}
