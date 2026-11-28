namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Models;
using Models.Pages;
using Shared.Utilities;

internal static class MediaFileLifecycle
{
    public const int ProcessedRetentionDays = 90;
    public const int FailedRetentionDays = 3;

    public static async Task<IDbContextTransaction?> BeginTransaction(ApplicationDbContext database,
        CancellationToken cancellationToken = default)
    {
        return database.Database.IsNpgsql() ? await database.Database.BeginTransactionAsync(cancellationToken) : null;
    }

    public static async Task<MediaFile?> FindForUpdate(ApplicationDbContext database, long id,
        CancellationToken cancellationToken = default)
    {
        if (database.Database.IsNpgsql())
        {
            return await database.MediaFiles.FromSqlInterpolated(
                    $"""SELECT * FROM media_files WHERE id = {id} FOR UPDATE""")
                .SingleOrDefaultAsync(cancellationToken);
        }

        return await database.MediaFiles.FindAsync([id], cancellationToken);
    }

    public static IQueryable<MediaFile> GetFilesForReference(ApplicationDbContext database, Guid[] ids)
    {
        // Hold shared locks until the reference is saved. A concurrent delete must then see the saved reference.
        if (database.Database.IsNpgsql() && database.Database.CurrentTransaction != null)
        {
            return database.MediaFiles.FromSqlInterpolated(
                $"""SELECT * FROM media_files WHERE global_id = ANY ({ids}) ORDER BY id FOR SHARE""");
        }

        return database.MediaFiles.Where(file => ids.Contains(file.GlobalId));
    }

    public static async Task<(Guid Id, string? DeletedName)?> FindUnavailablePageMedia(ApplicationDbContext database,
        string content)
    {
        var ids = new HashSet<Guid>();

        foreach (var link in MediaLinkParser.FindDestinations(content))
        {
            if (Guid.TryParse(link.GlobalId, out var id))
                ids.Add(id);
        }

        if (ids.Count == 0)
            return null;

        var media = await GetFilesForReference(database, [.. ids]).AsNoTracking()
            .Select(file => new { file.GlobalId, file.Name, file.Deleted }).ToListAsync();
        var deleted = media.FirstOrDefault(file => file.Deleted);
        if (deleted != null)
            return (deleted.GlobalId, deleted.Name);

        ids.ExceptWith(media.Select(file => file.GlobalId));
        if (ids.Count != 0)
            return (ids.First(), null);

        return null;
    }

    public static async Task<string?> FindUsage(ApplicationDbContext database, MediaFile file,
        CancellationToken cancellationToken = default)
    {
        var sitePart = await database.SiteLayoutParts.AsNoTracking().Where(part => part.ImageId == file.GlobalId)
            .OrderBy(part => part.Id).Select(part => new { part.Id, part.AltText, part.PartType })
            .FirstOrDefaultAsync(cancellationToken);

        if (sitePart != null)
            return $"site layout part \"{sitePart.AltText}\" ({sitePart.PartType}, ID {sitePart.Id})";

        // MediaFileUsages is refreshed asynchronously. Verify current content to catch recent edits and ignore
        // stale usages of images that have already been removed from a page.
        var dashedId = file.GlobalId.ToString();
        var compactId = file.GlobalId.ToString("N");
        var pages = database.VersionedPages.AsNoTracking().Where(page => !page.Deleted &&
                (page.LatestContent.Contains(dashedId) || page.LatestContent.Contains(compactId)))
            .OrderBy(page => page.Id).Select(page => new { page.Id, page.Title, page.LatestContent });

        await foreach (var page in pages.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            if (MediaLinkParser.FindDestinations(page.LatestContent)
                .Any(link => Guid.TryParse(link.GlobalId, out var id) && id == file.GlobalId))
            {
                return $"page \"{page.Title}\" (ID {page.Id})";
            }
        }

        return null;
    }
}
