namespace RevolutionaryWebApp.Server.Jobs;

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
///   Deletes dehydrated objects that are no longer needed by any builds
/// </summary>
[DisableConcurrentExecution(1800)]
public class DeleteUnneededDehydratedObjectsJob : IJob
{
    private readonly ILogger<DeleteUnneededDehydratedObjectsJob> logger;
    private readonly ApplicationDbContext database;
    private readonly IBackgroundJobClient jobClient;

    public DeleteUnneededDehydratedObjectsJob(ILogger<DeleteUnneededDehydratedObjectsJob> logger,
        ApplicationDbContext database, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        logger.LogInformation("Checking dehydrated objects to delete due to being unneeded");

        var dehydratedToDelete = await database.DehydratedObjects
            .FromSqlRaw(
                "SELECT * FROM dehydrated_objects WHERE NOT EXISTS (SELECT * FROM dehydrated_objects_dev_builds " +
                "WHERE dehydrated_objects.id = dehydrated_objects_dev_builds.dehydrated_objects_id)")
            .OrderBy(d => d.Id).Take(AppInfo.MaxDehydratedToDeleteAtOnce)
            .ToListAsync(cancellationToken);

        if (dehydratedToDelete.Count < 1)
        {
            logger.LogInformation("No dehydrated items to delete");
            return;
        }

        logger.LogInformation("Deleting {Count} dehydrated objects that are no longer needed",
            dehydratedToDelete.Count);

        if (dehydratedToDelete.Count >= AppInfo.MaxDehydratedToDeleteAtOnce)
        {
            logger.LogWarning(
                "Ran into max delete limit at once, hopefully over multiple days enough objects are deleted");
        }

        foreach (var dehydratedObject in dehydratedToDelete)
        {
            logger.LogDebug("Enqueueing job to delete storage item ({Id}) used by dehydrated object",
                dehydratedObject.StorageItemId);

            // This is scheduled much later to ensure that this delete triggers only after the dehydrated object has
            // been deleted as it requires the storage item to exist in the DB
            jobClient.Schedule<DeleteStorageItemByIdJob>(
                x => x.Execute(dehydratedObject.StorageItemId, CancellationToken.None), TimeSpan.FromMinutes(25));

            database.DehydratedObjects.Remove(dehydratedObject);
        }

        // Don't want to cancel the delete of objects from the DB
        // ReSharper disable once MethodSupportsCancellation
        await database.SaveChangesAsync();

        logger.LogInformation("Dehydrated object deletions saved, item delete jobs will delete their data soon");
    }
}
