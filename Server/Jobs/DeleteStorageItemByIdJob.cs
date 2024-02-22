namespace RevolutionaryWebApp.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Does a full delete on storage item by its id. <see cref="DeleteStorageItemJob.PerformProperDelete"/>
/// </summary>
public class DeleteStorageItemByIdJob
{
    private readonly ILogger<DeleteStorageItemByIdJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    public DeleteStorageItemByIdJob(ILogger<DeleteStorageItemByIdJob> logger, ApplicationDbContext database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(long itemId, CancellationToken cancellationToken)
    {
        var item = await database.StorageItems.Include(i => i.StorageItemVersions)
            .FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken);

        if (item == null)
        {
            logger.LogWarning("Could not find storage item to delete ({ItemId}), assuming already deleted", itemId);
            return;
        }

        DeleteStorageItemJob.PerformProperDelete(item, jobClient);

        logger.LogInformation("Enqueued jobs to delete storage item ({ItemId}) properly", itemId);
    }
}
