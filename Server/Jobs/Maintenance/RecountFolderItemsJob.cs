namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

public class RecountFolderItemsJob : MaintenanceJobBase
{
    public RecountFolderItemsJob(ILogger<RecountFolderItemsJob> logger,
        ApplicationDbContext operationDb, NotificationsEnabledDb operationStatusDb) : base(logger, operationDb,
        operationStatusDb)
    {
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        int updatedFolders = 0;

        // Due to needing to count items, we need to get the DB operation finished here so we load everything to memory
        // Hopefully there never is too many folders to fit into memory at once
        foreach (var folder in await database.StorageItems.Where(i => i.Ftype == FileType.Folder)
                     .ToListAsync(cancellationToken))
        {
            var newCount = await database.StorageItems.CountAsync(i => i.ParentId == folder.Id, cancellationToken);

            if (newCount != folder.Size)
            {
                cancellationToken.ThrowIfCancellationRequested();

                folder.Size = newCount;
                ++updatedFolders;
            }
        }

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Recounting folder items updated: {UpdatedFolders} folders(s)",
            updatedFolders);

        operationData.ExtendedDescription = $"Updated item counts for {updatedFolders} folder(s)";
    }
}
