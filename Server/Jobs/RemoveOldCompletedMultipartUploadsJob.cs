namespace ThriveDevCenter.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;

public class RemoveOldCompletedMultipartUploadsJob : IJob
{
    private readonly ILogger<RemoveOldCompletedMultipartUploadsJob> logger;
    private readonly ApplicationDbContext database;

    public RemoveOldCompletedMultipartUploadsJob(ILogger<RemoveOldCompletedMultipartUploadsJob> logger,
        ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - AppInfo.OldMultipartUploadThreshold;

        var items = await database.InProgressMultipartUploads.Where(i => i.UpdatedAt < cutoff && i.Finished)
            .ToListAsync(cancellationToken);

        if (items.Count < 1)
            return;

        database.InProgressMultipartUploads.RemoveRange(items);

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Cleared {Count} old multipart upload data", items.Count);
    }
}
