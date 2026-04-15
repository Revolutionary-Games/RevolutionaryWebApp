namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using DevCenterCommunication.Models.Enums;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

public class ClearOldCIImagesPrepareJob : MaintenanceJobBase
{
    private readonly IBackgroundJobClient jobClient;

    public ClearOldCIImagesPrepareJob(ILogger<ClearOldCIImagesPrepareJob> logger,
        ApplicationDbContext operationDb, NotificationsEnabledDb operationStatusDb,
        IBackgroundJobClient jobClient) : base(logger, operationDb, operationStatusDb)
    {
        this.jobClient = jobClient;
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        // Locate the CI/Images folder in StorageItems
        var imagesFolder = await StorageItem.FindByPath(database, "CI/Images");

        if (imagesFolder == null)
        {
            operationData.ExtendedDescription = "No CI/Images folder found (nothing to do)";
            return;
        }

        // Gather all descendant items under CI/Images (BFS)
        var toVisit = new Queue<long>();
        toVisit.Enqueue(imagesFolder.Id);

        var affected = 0;

        while (toVisit.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var parentId = toVisit.Dequeue();

            var children = await database.StorageItems
                .Where(i => i.ParentId == parentId)
                .Select(i => new { i.Id, i.Ftype, i.WriteAccess })
                .ToListAsync(cancellationToken);

            foreach (var child in children)
            {
                // Enqueue folders for traversal
                if (child.Ftype == FileType.Folder)
                {
                    toVisit.Enqueue(child.Id);
                }
                else
                {
                    // Make files admin-writable and bump timestamp
                    var item = await database.StorageItems.FirstAsync(i => i.Id == child.Id, cancellationToken);

                    if (item.WriteAccess != FileAccess.OwnerOrAdmin)
                    {
                        item.WriteAccess = FileAccess.OwnerOrAdmin;
                        ++affected;

                        logger.LogInformation("Marking CI image file {Name} ({Id}) as admin-writable", item.Name,
                            item.Id);

                        item.BumpUpdatedAt();
                    }
                }
            }
        }

        await database.SaveChangesAsync(cancellationToken);

        // Schedule the clean-up in 90 days as a standalone job (not tied to this maintenance operation)
        jobClient.Schedule<ClearOldCIImagesCleanupJob>(x => x.Execute(CancellationToken.None),
            TimeSpan.FromDays(90));

        logger.LogInformation("ClearOldCIImages prepare phase affected {Count} file(s). Cleanup scheduled in 90 days.",
            affected);

        operationData.ExtendedDescription =
            $"Marked {affected} CI image file(s) for deletion. Cleanup scheduled in 90 days.";
    }
}
