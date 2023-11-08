namespace ThriveDevCenter.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

[DisableConcurrentExecution(900)]
public class CleanOldPrecompiledObjectVersionsJob : IJob
{
    private readonly ILogger<CleanOldPrecompiledObjectVersionsJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    public CleanOldPrecompiledObjectVersionsJob(ILogger<CleanOldPrecompiledObjectVersionsJob> logger,
        ApplicationDbContext database, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var deleteCutoff = DateTime.UtcNow - AppInfo.DeleteUnusedPrecompiledAfter;

        var toDelete = await database.PrecompiledObjectVersions
            .Where(v => v.CreatedAt < deleteCutoff && (v.LastDownload == null || v.LastDownload < deleteCutoff))
            .Include(v => v.StoredInItem).ThenInclude(i => i.StorageItemVersions)
            .ToListAsync(cancellationToken);

        if (toDelete.Count < 1)
            return;

        logger.LogInformation("Deleting {Count} old non-downloaded precompiled objects", toDelete.Count);

        foreach (var objectVersion in toDelete)
        {
            logger.LogInformation("Deleting PrecompiledObject {Identifier}", objectVersion.StorageFileName);

            DeletePrecompiledObjectVersionIfUploadFailed.DeletePrecompiledObjectVersion(objectVersion, jobClient);

            if (cancellationToken.IsCancellationRequested)
                break;
        }
    }
}
