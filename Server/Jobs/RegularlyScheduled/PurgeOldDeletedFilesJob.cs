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

/// <summary>
///   Permanently deletes (purges) items in the trash folder that have been there for a while
/// </summary>
[DisableConcurrentExecution(1800)]
public class PurgeOldDeletedFilesJob : IJob
{
    private readonly ILogger<PurgeOldDeletedFilesJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    public PurgeOldDeletedFilesJob(ILogger<PurgeOldDeletedFilesJob> logger, ApplicationDbContext database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.DeleteTrashedItemsAfter;

        var filesToPurge = await database.StorageItems.Include(i => i.StorageItemVersions).Include(d => d.DeleteInfo)
            .Where(i => i.Deleted && i.DeleteInfo != null && i.DeleteInfo.DeletedAt < cutoff && !i.Special)
            .AsNoTracking().ToListAsync(cancellationToken);

        if (filesToPurge.Count < 1)
            return;

        logger.LogInformation("Purging {Count} storage items permanently that were in the trash for a while",
            filesToPurge.Count);

        foreach (var item in filesToPurge)
        {
            logger.LogInformation("Permanently deleting file that's been long in trash: {Name} ({Id})", item.Name,
                item.Id);

            DeleteStorageItemJob.PerformProperDelete(item, jobClient);

            if (cancellationToken.IsCancellationRequested)
                break;
        }
    }
}
