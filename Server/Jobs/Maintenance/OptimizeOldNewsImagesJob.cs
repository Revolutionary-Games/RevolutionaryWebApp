namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Models;
using Hangfire;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Shared.Models.Pages;

public class OptimizeOldNewsImagesJob : MaintenanceJobBase
{
    private const int BatchSize = 100;

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UsePipeTables().UseGridTables().UsePreciseSourceLocation().Build();

    private readonly IBackgroundJobClient jobClient;

    public OptimizeOldNewsImagesJob(ILogger<OptimizeOldNewsImagesJob> logger, ApplicationDbContext operationDb,
        NotificationsEnabledDb operationStatusDb, IBackgroundJobClient jobClient)
        : base(logger, operationDb, operationStatusDb)
    {
        this.jobClient = jobClient;
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddMonths(-6);
        var eligiblePosts = Database.VersionedPages.Where(page => page.Type == PageType.Post && !page.Deleted &&
            page.Visibility != PageVisibility.HiddenDraft && page.PublishedAt < cutoff &&
            page.LatestContent.Contains("media:"));

        long lastId = 0;
        int updatedPages = 0;
        int updatedImages = 0;

        while (true)
        {
            var batchCursor = lastId;
            var ids = await eligiblePosts.Where(page => page.Id > batchCursor).OrderBy(page => page.Id)
                .Select(page => page.Id).Take(BatchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0)
                break;

            foreach (var id in ids)
            {
                var changes = await OptimizePage(eligiblePosts, id, operationData.PerformedById, cancellationToken);
                if (changes > 0)
                {
                    ++updatedPages;
                    updatedImages += changes;
                }
            }

            lastId = ids[^1];
        }

        operationData.ExtendedDescription = $"Updated {updatedImages} image link(s) in {updatedPages} news post(s).";
    }

    private static IEnumerable<(Guid Id, int ExtensionStart)> FindPngMediaImages(string content)
    {
        var markdown = Markdown.Parse(content, MarkdownPipeline);
        foreach (var link in markdown.Descendants<LinkInline>())
        {
            var url = link.Url;
            if (!link.IsImage || url == null || !url.StartsWith("media:", StringComparison.Ordinal))
                continue;

            var start = link.UrlSpan.Start;
            if (start < link.Span.Start + 2 || link.UrlSpan.End > link.Span.End ||
                content.AsSpan(start - 2, 2) is not "](" ||
                !content.AsSpan(start, link.UrlSpan.Length).SequenceEqual(url))
            {
                continue;
            }

            // The custom media link syntax allows an optional dot and a full-view flag.
            var parts = url.Split(':');
            if ((parts.Length != 3 && (parts.Length != 5 || parts[3] != "f" || parts[4] is not ("y" or "n"))) ||
                parts[1] is not ("png" or ".png") || !Guid.TryParseExact(parts[2], "D", out var id))
            {
                continue;
            }

            yield return (id, start + (parts[1][0] == '.' ? "media:.".Length : "media:".Length));
        }
    }

    private async Task<int> OptimizePage(IQueryable<VersionedPage> eligiblePosts, long id, long? editorId,
        CancellationToken cancellationToken)
    {
        await using var transaction = Database.Database.IsRelational() ?
            await Database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) :
            null;

        var page = await eligiblePosts.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (page == null)
            return 0;

        try
        {
            var candidates = FindPngMediaImages(page.LatestContent).ToList();
            if (candidates.Count == 0)
                return 0;

            var ids = candidates.Select(candidate => candidate.Id).Distinct().ToList();
            var pngIds = await Database.MediaFiles
                .Where(file => ids.Contains(file.GlobalId) && file.Processed && !file.Deleted &&
                    file.Name.EndsWith(".png"))
                .Select(file => file.GlobalId).ToHashSetAsync(cancellationToken);
            if (pngIds.Count == 0)
                return 0;

            var originalContent = page.LatestContent;
            var content = new StringBuilder(originalContent);
            int changes = 0;
            foreach (var candidate in candidates.OrderByDescending(candidate => candidate.ExtensionStart))
            {
                if (!pngIds.Contains(candidate.Id))
                    continue;

                content.Remove(candidate.ExtensionStart, "png".Length).Insert(candidate.ExtensionStart, "webp");
                ++changes;
            }

            page.LatestContent = content.ToString();
            var previousVersion = page.CreatePreviousVersion(await page.GetCurrentVersion(Database), originalContent);
            previousVersion.EditedById = page.LastEditorId;
            previousVersion.EditComment = page.LastEditComment;

            page.LastEditorId = editorId;
            page.LastEditComment = "Converted PNG image previews to WebP";
            page.BumpUpdatedAt();
            await Database.PageVersions.AddAsync(previousVersion, cancellationToken);
            await Database.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);

            Database.Entry(previousVersion).State = EntityState.Detached;
            page.OnEdited(jobClient);
            return changes;
        }
        finally
        {
            Database.Entry(page).State = EntityState.Detached;
        }
    }
}
