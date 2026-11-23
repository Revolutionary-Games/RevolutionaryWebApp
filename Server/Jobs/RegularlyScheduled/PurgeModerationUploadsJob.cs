namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;

[DisableConcurrentExecution(1000)]
public class PurgeModerationUploadsJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IGeneralRemoteStorage generalStorage;
    private readonly IMediaStorage mediaStorage;
    private readonly IUploadFileStorage uploadStorage;
    private readonly ILogger<PurgeModerationUploadsJob> logger;

    public PurgeModerationUploadsJob(ApplicationDbContext database, IGeneralRemoteStorage generalStorage,
        IMediaStorage mediaStorage, IUploadFileStorage uploadStorage, ILogger<PurgeModerationUploadsJob> logger)
    {
        this.database = database;
        this.generalStorage = generalStorage;
        this.mediaStorage = mediaStorage;
        this.uploadStorage = uploadStorage;
        this.logger = logger;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var deletions = await database.ModerationUploadDeletions.Where(item => item.CompletedAt == null &&
            item.DeleteAfter <= now).OrderBy(item => item.DeleteAfter).Take(500).ToListAsync(cancellationToken);
        foreach (var deletion in deletions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IBaseRemoteStorage storage = deletion.Storage switch
            {
                ModerationUploadStorage.General => generalStorage,
                ModerationUploadStorage.Media => mediaStorage,
                ModerationUploadStorage.Temporary => uploadStorage,
                _ => throw new InvalidOperationException("Invalid moderation storage"),
            };
            if (!storage.Configured)
                continue;

            try
            {
                await storage.DeleteObject(deletion.Path);
                deletion.CompletedAt = DateTime.UtcNow;
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Failed to purge moderation upload {DeletionId}; will retry",
                    deletion.Id);
            }
        }
    }
}
