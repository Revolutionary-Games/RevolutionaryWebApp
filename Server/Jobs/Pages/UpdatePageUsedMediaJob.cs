namespace RevolutionaryWebApp.Server.Jobs.Pages;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Shared.Utilities;

/// <summary>
///   Refreshes <see cref="MediaFileUsage"/> for a page
/// </summary>
public class UpdatePageUsedMediaJob
{
    private readonly ILogger<UpdatePageUsedMediaJob> logger;
    private readonly ApplicationDbContext database;

    public UpdatePageUsedMediaJob(ILogger<UpdatePageUsedMediaJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public static IEnumerable<string> GetUsedResourceGUIDs(string content)
    {
        return MediaLinkParser.FindDestinations(content).Select(link => link.GlobalId);
    }

    public async Task Execute(long pageId, CancellationToken cancellationToken)
    {
        var page = await database.VersionedPages.AsNoTracking().Where(p => p.Id == pageId)
            .FirstOrDefaultAsync(cancellationToken);

        if (page == null)
        {
            logger.LogWarning("Failed to get page ({PageId}) for media update, assuming it is deleted", pageId);
            return;
        }

        var newIds = GetUsedResourceGUIDs(page.LatestContent).ToList();

        var parsedIds = new HashSet<Guid>();
        foreach (var id in newIds)
        {
            if (Guid.TryParse(id, out var guid))
            {
                parsedIds.Add(guid);
            }
            else
            {
                logger.LogWarning("Failed to parse media id ({Id}) for page ({PageId})", id, pageId);
            }
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            HashSet<Guid> existingMediaIds = parsedIds.Count == 0 ? [] : await database.MediaFiles
                .Where(file => parsedIds.Contains(file.GlobalId))
                .Select(file => file.GlobalId).ToHashSetAsync(cancellationToken);

            foreach (var missingId in parsedIds.Except(existingMediaIds))
            {
                logger.LogWarning("Skipping missing media file ({MediaId}) referenced by page ({PageId})",
                    missingId, pageId);
            }

            var alreadyExistingLinks = await database.MediaFileUsages
                .AsNoTracking()
                .Where(u => u.UsedByResource == pageId && u.Usage == MediaFileUsage.UsageType.Page)
                .Select(u => u.MediaFileGuid).ToHashSetAsync(cancellationToken);

            var toAdd = existingMediaIds.Except(alreadyExistingLinks).ToList();
            var toRemove = alreadyExistingLinks.Except(existingMediaIds).ToList();

            // Batch-add new usage records instead of individual inserts
            if (toAdd.Count > 0)
            {
                var newUsages = toAdd.Select(id => new MediaFileUsage
                {
                    MediaFileGuid = id,
                    UsedByResource = pageId,
                    Usage = MediaFileUsage.UsageType.Page,
                }).ToList();

                await database.MediaFileUsages.AddRangeAsync(newUsages, cancellationToken);
            }

            if (toRemove.Count > 0)
            {
                // Optimized remove
                await database.MediaFileUsages
                    .Where(u => u.UsedByResource == pageId && u.Usage == MediaFileUsage.UsageType.Page &&
                        toRemove.Contains(u.MediaFileGuid)).ExecuteDeleteAsync(cancellationToken);
            }

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Updated media used by page ({PageId}), it uses {Count} media resources", pageId,
                existingMediaIds.Count);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
