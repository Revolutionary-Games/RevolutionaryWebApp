namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using Services;

public class MakeSureNoMultipartPartsExistJob
{
    private readonly ILogger<MakeSureNoMultipartPartsExistJob> logger;
    private readonly GeneralRemoteStorage remoteStorage;
    private readonly IBackgroundJobClient jobClient;

    public MakeSureNoMultipartPartsExistJob(ILogger<MakeSureNoMultipartPartsExistJob> logger,
        GeneralRemoteStorage remoteStorage, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.remoteStorage = remoteStorage;
        this.jobClient = jobClient;
    }

    public async Task Execute(string uploadId, string path, CancellationToken cancellationToken)
    {
        logger.LogInformation("Making sure no multipart parts exist for {UploadId}", uploadId);

        bool hadParts = false;

        try
        {
            var parts = await remoteStorage.ListMultipartUploadParts(path, uploadId);
            if (parts.Count < 1)
            {
                logger.LogInformation("No parts detected for the multipart upload");

                // But still try to delete it
            }
            else
            {
                hadParts = true;
            }
        }
        catch (Exception e)
        {
            logger.LogInformation(e, "Couldn't list parts for upload {UploadId}, assuming no parts exist",
                uploadId);
            return;
        }

        try
        {
            await remoteStorage.AbortMultipartUpload(path, uploadId);

            if (hadParts)
            {
                // Try to delete it one more time just to be extra safe
                jobClient.Schedule<MakeSureNoMultipartPartsExistJob>(
                    x => x.Execute(uploadId, path, CancellationToken.None),
                    TimeSpan.FromDays(1));
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed to issue abort call for multipart upload {UploadId}", uploadId);
        }
    }
}