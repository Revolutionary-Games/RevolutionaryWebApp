namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Pages;
using Models.Translations;
using SharedBase.Utilities;
using Utilities;

public static class ModerationRevisionJournal
{
    private const string PageContentProperty = nameof(VersionedPage.LatestContent);

    /// <summary>
    ///   Creates records of all models that don't have history models, and puts them into the moderation revision
    ///   journal so that user purges can revert all of the user's changes.
    /// </summary>
    /// <param name="database">Database to record from (skipped if the DB is suppressing journalling)</param>
    public static async Task Record(ApplicationDbContext database)
    {
        if (database.SuppressModerationJournal)
            return;

        var revisions = new List<ModerationRevision>();

        var entries = database.ChangeTracker.Entries()
            .Where(entry => entry.State == EntityState.Modified ||
                (entry.State == EntityState.Added && entry.Entity is PageVersion)).ToList();

        foreach (var entry in entries.Where(entry => entry.State == EntityState.Modified))
        {
            // Pick only specific models that don't have history models but do have an actor user.
            var actorProperty = entry.Entity switch
            {
                // Translation values and source edits are reverted from TranslationChange.
                DictionaryTerm or DictionaryTermTranslation or DictionaryLanguageTerm => "LastEditedByUserId",
                VersionedPage => "LastEditorId",
                StorageItem or MediaFile or MediaFolder => "LastModifiedById",
                _ => null,
            };

            if (actorProperty == null || entry.Metadata.FindProperty(actorProperty) == null)
                continue;

            var actor = entry.Property(actorProperty).CurrentValue as long?;
            if (actor == null)
                continue;

            var changed = entry.Properties.Where(property => property.IsModified &&
                !property.Metadata.IsConcurrencyToken && !property.Metadata.IsPrimaryKey()).ToList();
            if (changed.Count == 0)
                continue;

            // Deletion and permanent purge transitions belong to the media lifecycle.
            if (entry.Entity is MediaFile && changed.Any(property => property.Metadata.Name is
                    nameof(MediaFile.Deleted) or nameof(MediaFile.DeletedAt) or nameof(MediaFile.PurgeStarted)))
            {
                continue;
            }

            var before = changed.ToDictionary(property => property.Metadata.Name, property => property.OriginalValue);
            var after = changed.ToDictionary(property => property.Metadata.Name, property => property.CurrentValue);

            if (entry.Entity is VersionedPage page && before.ContainsKey(PageContentProperty))
            {
                before.Remove(PageContentProperty);
                after.Remove(PageContentProperty);
                var previousVersions = entries.Where(item => item.State == EntityState.Added)
                    .Select(item => item.Entity).OfType<PageVersion>()
                    .Where(version => version.PageId == page.Id).Take(2).ToList();
                if (previousVersions.Count == 1)
                {
                    // A numeric before value references the native reverse diff. Keep a hash for the later-edit check.
                    before[PageContentProperty] = previousVersions[0].Version;
                    after[PageContentProperty] = HashPageContent(page.LatestContent);
                }
            }

            if (before.Count == 0)
                continue;

            revisions.Add(new ModerationRevision
            {
                UserId = actor, EntityType = entry.Metadata.ClrType.Name,
                KeyJson = JsonSerializer.Serialize(entry.Metadata.FindPrimaryKey()!.Properties
                    .Select(property => entry.Property(property.Name).CurrentValue).ToArray()),
                BeforeJson = JsonSerializer.Serialize(before),
                AfterJson = JsonSerializer.Serialize(after),
            });
        }

        foreach (var revision in revisions)
            await database.ModerationRevisions.AddAsync(revision);
    }

    public static async Task<int> Revert(ApplicationDbContext database, long userId,
        DateTime cutoff)
    {
        var skipped = 0;
        var revisions = await database.ModerationRevisions.Where(revision => revision.UserId == userId &&
                revision.CreatedAt >= cutoff)
            .OrderByDescending(revision => revision.CreatedAt).ThenByDescending(revision => revision.Id).ToListAsync();
        foreach (var revision in revisions)
        {
            var type = database.Model.GetEntityTypes().Single(entity => entity.ClrType.Name == revision.EntityType);
            var key = JsonSerializer.Deserialize<JsonElement[]>(revision.KeyJson)!;
            var properties = type.FindPrimaryKey()!.Properties;
            var values = key.Select((element, index) => element.Deserialize(properties[index].ClrType)).ToArray();
            var finder = typeof(ModerationRevisionJournal).GetMethod(nameof(FindIncludingDeleted),
                BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(type.ClrType);
            var entity = await (Task<object?>)finder.Invoke(null, [database, values])!;
            if (entity == null)
                continue;

            var entry = database.Entry(entity);
            var after = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(revision.AfterJson)!;
            var before = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(revision.BeforeJson)!;
            var versionedContent = before.TryGetValue(PageContentProperty, out var contentBefore) &&
                contentBefore.ValueKind == JsonValueKind.Number;
            if (after.Any(pair => versionedContent && pair.Key == PageContentProperty ?
                    HashPageContent(((VersionedPage)entity).LatestContent) != pair.Value.GetString() :
                    !Equals(entry.Property(pair.Key).CurrentValue,
                        pair.Value.Deserialize(entry.Property(pair.Key).Metadata.ClrType))))
            {
                // A later edit by another contributor takes precedence.
                if (revision.RevertedAt == null)
                    ++skipped;
                continue;
            }

            if (versionedContent)
            {
                var page = (VersionedPage)entity;
                var version = contentBefore.GetInt32();
                var previous = await database.PageVersions.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(item => item.PageId == page.Id && item.Version == version);
                if (previous == null)
                {
                    if (revision.RevertedAt == null)
                        ++skipped;
                    continue;
                }

                var restoredContent = DiffGenerator.Default.ApplyDiff(page.LatestContent, previous.DecodeDiffData());
                if (await MediaFileLifecycle.FindUnavailablePageMedia(database, restoredContent) != null)
                {
                    if (revision.RevertedAt == null)
                        ++skipped;
                    continue;
                }

                page.LatestContent = restoredContent;
                before.Remove(PageContentProperty);
            }

            foreach (var pair in before)
            {
                var property = entry.Property(pair.Key);
                property.CurrentValue = pair.Value.Deserialize(property.Metadata.ClrType);
            }

            revision.RevertedAt = DateTime.UtcNow;
        }

        // Use fresh optimistic concurrency versions after replaying the entire chain.
        foreach (var entry in database.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Modified))
        {
            if (entry.Metadata.FindProperty("Version")?.ClrType == typeof(Guid))
                entry.Property("Version").CurrentValue = Guid.NewGuid();
        }

        return skipped;
    }

    private static string HashPageContent(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static async Task<object?> FindIncludingDeleted<T>(ApplicationDbContext database, object?[] values)
        where T : class
    {
        var type = database.Model.FindEntityType(typeof(T))!;
        var keys = type.FindPrimaryKey()!.Properties;
        var parameter = Expression.Parameter(typeof(T), "entity");

        Expression? condition = null;
        for (var index = 0; index < keys.Count; ++index)
        {
            var comparison = Expression.Equal(Expression.Property(parameter, keys[index].Name),
                Expression.Constant(values[index], keys[index].ClrType));
            condition = condition == null ? comparison : Expression.AndAlso(condition, comparison);
        }

        var predicate = Expression.Lambda<Func<T, bool>>(condition!, parameter);
        return await database.Set<T>().IgnoreQueryFilters().SingleOrDefaultAsync(predicate);
    }
}
