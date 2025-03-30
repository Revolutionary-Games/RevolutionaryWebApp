namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using Filters;
using Hangfire;
using Hubs;
using Jobs.Pages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;
using SharedBase.Utilities;
using Utilities;

/// <summary>
///   Base type for all controllers that manage <see cref="VersionedPage"/>
/// </summary>
public abstract class BasePageController : Controller
{
    // Note that there are similar regexes in MarkdownBBCodeService that need to be updated if these are
    public static readonly Regex MediaLinkCountingRegex =
        new(@"media:[\w\.]+:[a-f0-9\-]+", RegexOptions.Compiled, TimeSpan.FromSeconds(5));

    public static readonly Regex MediaLinkIDExtractingRegex =
        new(@"media:[\w\.]+:([a-f0-9\-]+)", RegexOptions.Compiled, TimeSpan.FromSeconds(10));

    protected readonly NotificationsEnabledDb Database;
    protected readonly IBackgroundJobClient JobClient;
    protected readonly IHubContext<NotificationsHub, INotifications> Notifications;

    public BasePageController(NotificationsEnabledDb database, IBackgroundJobClient jobClient,
        IHubContext<NotificationsHub, INotifications> notifications)
    {
        Database = database;
        JobClient = jobClient;
        Notifications = notifications;
    }

    /// <summary>
    ///   Set to false when unit testing with an in-memory database that doesn't support transactions
    /// </summary>
    public bool UsePageUpdateTransaction { get; set; } = true;

    protected abstract ILogger Logger { get; }
    protected abstract PageType HandledPageType { get; }

    protected abstract GroupType PrimaryPublisherGroupType { get; }
    protected abstract GroupType ExtraAccessGroup { get; }

    [NonAction]
    public static bool CheckPageContentRespectsLimits(string content, out ActionResult? failure)
    {
        // Count images and fail if more than 100
        var imageCount = MediaLinkCountingRegex.Matches(content).Count;

        if (imageCount > 100)
        {
            failure = new BadRequestObjectResult("Pages may at most use 100 images");
            return false;
        }

        failure = null;
        return true;
    }

    /// <summary>
    ///   Get a List of pages. Should be overridden in base classes, otherwise this endpoint is exposed without
    ///   authentication.
    /// </summary>
    /// <returns>Page info objects</returns>
    [HttpGet]
    public virtual async Task<PagedResult<VersionedPageInfo>> GetList([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize, bool deleted = false)
    {
        IQueryable<VersionedPage> query;

        try
        {
            query = Database.VersionedPages.AsNoTracking().OrderBy(sortColumn, sortDirection)
                .Where(p => p.Deleted == deleted && p.Type == HandledPageType);
        }
        catch (ArgumentException e)
        {
            Logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        // Remove big objects from the result (and for convenience convert to the info type at the same time)
        var infoQuery = query.Select(p => new VersionedPageInfo
        {
            Id = p.Id,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            Title = p.Title,
            Visibility = p.Visibility,
            Permalink = p.Permalink,
            PublishedAt = p.PublishedAt,
            CreatorId = p.CreatorId,
            LastEditorId = p.LastEditorId,
        });

        var objects = await infoQuery.ToPagedResultAsync(page, pageSize);

        return objects;
    }

    [HttpGet("{id:long}")]
    public virtual async Task<ActionResult<VersionedPageDTO>> GetSingle([Required] long id)
    {
        var page = await Database.VersionedPages.FindAsync(id);

        if (page == null || page.Type != HandledPageType)
            return NotFound();

        var version = await page.GetCurrentVersion(Database);

        return page.GetDTO(version);
    }

    [HttpPost]
    public virtual async Task<ActionResult> CreatePage([Required] [FromBody] VersionedPageDTO pageDTO)
    {
        if (pageDTO.Visibility != PageVisibility.HiddenDraft)
        {
            return BadRequest("Page cannot start in visible mode");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (await Database.VersionedPages.AsNoTracking().FirstOrDefaultAsync(p =>
                p.Title == pageDTO.Title || (p.Permalink != null && p.Permalink == pageDTO.Permalink)) != null)
        {
            return BadRequest("Page with the given title or permalink already exists");
        }

        if (pageDTO.Permalink != null && !ValidatePermalink(pageDTO.Permalink, out var fail))
            return fail;

        // Note that if initial content was to be allowed, CheckPageContentRespectsLimits must be called here
        // And also would need to refresh used media

        var page = new VersionedPage(pageDTO.Title)
        {
            Visibility = pageDTO.Visibility,
            Permalink = pageDTO.Permalink,
            Type = HandledPageType,
            CreatorId = user.Id,
            LastEditComment = "Initial version",
            LastEditorId = user.Id,
        };

        await Database.VersionedPages.AddAsync(page);

        await Database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"New page (\"{page.Title.Truncate()}\" type: {page.Type}) created")
            {
                PerformedById = user.Id,
                Extended = $"Title: {page.Title}, permalink: {page.Permalink}",
            });

        await Database.SaveChangesAsync();

        return Ok(page.Id.ToString());
    }

    /// <summary>
    ///   Edits a page by user. Note that the first edit for an empty page doesn't create a version history entry.
    /// </summary>
    /// <param name="id">Page to edit</param>
    /// <param name="pageDTO">Updated data</param>
    /// <returns>Action result indicating success</returns>
    [HttpPut("{id:long}")]
    public virtual async Task<ActionResult> UpdatePage([Required] long id,
        [Required] [FromBody] VersionedPageDTO pageDTO)
    {
        IDbContextTransaction? transaction = null;

        // Maybe a transaction here helps against unintended duplicate edits
        if (UsePageUpdateTransaction)
            transaction = await Database.Database.BeginTransactionAsync();

        var page = await Database.VersionedPages.FindAsync(id);

        if (page == null)
            return NotFound();

        if (!CanEdit(page, pageDTO, out var editFail))
            return editFail;

        bool newlyPublished = false;

        if (pageDTO.Visibility != PageVisibility.HiddenDraft)
        {
            // Needs to have a permalink when a page is visible
            if (string.IsNullOrWhiteSpace(pageDTO.Permalink))
            {
                // Reuse an existing permalink if there is one
                if (string.IsNullOrWhiteSpace(page.Permalink))
                {
                    page.Permalink = VersionedPageDTO.GeneratePermalinkFromTitle(pageDTO.Title);
                }

                pageDTO.Permalink = page.Permalink;
            }

            // Published at is set the first time the page is saved when visible
            if (page.PublishedAt == null)
            {
                page.PublishedAt = DateTime.UtcNow;
                newlyPublished = true;
            }
        }
        else if (string.IsNullOrWhiteSpace(pageDTO.Permalink))
        {
            // Don't override permalink if it is set already
            pageDTO.Permalink = page.Permalink;
        }

        if (pageDTO.Permalink != null && !ValidatePermalink(pageDTO.Permalink, out var fail))
            return fail;

        // Enforce content limits so that someone doesn't create a page with hundred thousand images
        if (!CheckPageContentRespectsLimits(pageDTO.LatestContent, out var failure))
        {
            if (failure != null)
                return failure;

            return BadRequest("Page content exceeds allowed limits (unknown which limit was violated)");
        }

        if (await Database.VersionedPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id != page.Id &&
                (p.Title == pageDTO.Title || (p.Permalink != null && p.Permalink == pageDTO.Permalink))) != null)
        {
            return BadRequest("Page with the given updated title or permalink already exists");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (pageDTO.LatestContent != page.LatestContent)
        {
            // Updating page content
            int version = await page.GetCurrentVersion(Database);

            if (version != pageDTO.VersionNumber)
            {
                return BadRequest($"Page version mismatch. Someone has edited the page while you were editing it. " +
                    $"Your version: {pageDTO.VersionNumber} is not the expected: {version}. Please open a new tab, " +
                    $"copy your changes to it and save there. If this is a problem often it would be possible to add " +
                    $"automatic edit merging in many cases.");
            }

            var previousContent = page.LatestContent;
            var previousComment = page.LastEditComment;
            var previousAuthor = page.LastEditorId;

            var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(page, pageDTO);

            if (!changes)
                return Problem("Changes to the model should have been applied, but didn't");

            page.LastEditorId = user.Id;
            page.LastEditComment = pageDTO.LastEditComment;
            page.BumpUpdatedAt();

            // Added a new version, create a version from the old one
            if (!string.IsNullOrWhiteSpace(previousContent))
            {
                // Create edit history from the previous version to preserve the old content in case it needs to be
                // reverted to
                var previousVersion = page.CreatePreviousVersion(version, previousContent);
                previousVersion.EditedById = previousAuthor;
                previousVersion.EditComment = previousComment;

                await Database.PageVersions.AddAsync(previousVersion);

                ++version;

                await Database.ActionLogEntries.AddAsync(new ActionLogEntry(
                    $"Page {page.Id} (\"{page.Title.Truncate()}\") edited, version is now: {version}",
                    description)
                {
                    PerformedById = user.Id,
                });
            }
            else
            {
                // This is the initial version / replaces the previous version as it was empty
                await Database.ActionLogEntries.AddAsync(new ActionLogEntry(
                    $"Page {page.Id} (\"{page.Title.Truncate()}\") edited, last version was blank, " +
                    $"version is now: {version}", description)
                {
                    PerformedById = user.Id,
                });
            }

            await EditNotificationsController.SendEditNotice(Notifications, user, page.Id, true);
        }
        else
        {
            // Updating just page properties
            var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(page, pageDTO);

            if (!changes)
                return Ok("No changes");

            page.BumpUpdatedAt();

            await Database.ActionLogEntries.AddAsync(
                new ActionLogEntry($"Page {page.Id} (\"{page.Title.Truncate()}\") properties updated", description)
                {
                    PerformedById = user.Id,
                });
        }

        await Database.SaveChangesAsync();

        if (transaction != null)
            await transaction.CommitAsync();

        page.OnEdited(JobClient);

        if (newlyPublished)
        {
            JobClient.Schedule<OnNewPagePublishedJob>(x => x.Execute(page.Id, CancellationToken.None),
                TimeSpan.FromSeconds(65));
        }

        return Ok();
    }

    [HttpDelete("{id:long}")]
    public virtual async Task<ActionResult> DeleteResource([Required] long id)
    {
        var page = await Database.VersionedPages.FindAsync(id);

        if (page == null || page.Type != HandledPageType)
            return NotFound();

        if (page.Deleted)
            return BadRequest("Page is already deleted");

        if (page.Visibility != PageVisibility.HiddenDraft)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(PrimaryPublisherGroupType,
                    AuthenticationScopeRestriction.None) &&
                !HttpContext.HasAuthenticatedUserWithGroup(ExtraAccessGroup, AuthenticationScopeRestriction.None))
            {
                return this.WorkingForbid("Published pages can only be deleted by users with publishing permission");
            }
        }

        page.Deleted = true;
        page.BumpUpdatedAt();

        await Database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"Page {page.Id} (\"{page.Title.Truncate()}\") deleted")
            {
                PerformedById = HttpContext.AuthenticatedUserOrThrow().Id,
            });

        await Database.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id:long}/restore")]
    public virtual async Task<ActionResult> RestoreResource([Required] long id)
    {
        var page = await Database.VersionedPages.FindAsync(id);

        if (page == null || page.Type != HandledPageType)
            return NotFound();

        if (!page.Deleted)
            return BadRequest("Page is not in deleted state");

        if (page.Visibility != PageVisibility.HiddenDraft)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(PrimaryPublisherGroupType,
                    AuthenticationScopeRestriction.None) &&
                !HttpContext.HasAuthenticatedUserWithGroup(ExtraAccessGroup, AuthenticationScopeRestriction.None))
            {
                return this.WorkingForbid("Published pages can only be restored by users with publish permission");
            }
        }

        page.Deleted = false;
        page.BumpUpdatedAt();

        await Database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"Page {page.Id} (\"{page.Title.Truncate()}\") restored")
            {
                PerformedById = HttpContext.AuthenticatedUserOrThrow().Id,
            });

        await Database.SaveChangesAsync();

        return Ok();
    }

    /// <summary>
    ///   Lists historical versions. It is recommended by default to sort by version number in descending order.
    /// </summary>
    /// <returns>List of historical version info</returns>
    [HttpGet("{id:long}/versions")]
    public virtual async Task<ActionResult<PagedResult<PageVersionInfo>>> ListResourceVersions([Required] long id,
        [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 100)] int pageSize)
    {
        var parentPage = await Database.VersionedPages.FindAsync(id);

        if (parentPage == null || parentPage.Type != HandledPageType)
            return NotFound();

        IQueryable<PageVersion> query;

        try
        {
            query = Database.PageVersions.AsNoTracking().Where(v => v.PageId == id).OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            Logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        // Remove big objects from the result (and for convenience convert to the info type at the same time)
        var infoQuery = query.Select(p => new PageVersionInfo
        {
            PageId = p.PageId,
            Version = p.Version,
            EditComment = p.EditComment,
            Deleted = p.Deleted,
            EditedById = p.EditedById,
            CreatedAt = p.CreatedAt,
        });

        var objects = await infoQuery.ToPagedResultAsync(page, pageSize);

        return objects;
    }

    /// <summary>
    ///   Gets historical version of a page. This is a POST request as generating the old text may be expensive if
    ///   there are a lot of newer versions.
    /// </summary>
    /// <returns>The generated old version data or an error</returns>
    [HttpPost("{id:long}/versions/{version:int}")]
    public virtual async Task<ActionResult<PageVersionDTO>> GetResourceHistoricalVersion([Required] long id,
        [Required] int version)
    {
        var parentPage = await Database.VersionedPages.FindAsync(id);

        if (parentPage == null || parentPage.Type != HandledPageType)
            return NotFound();

        if (parentPage.Deleted)
            return BadRequest("Page is in deleted state");

        var pageVersion = await Database.PageVersions.FindAsync(id, version);

        if (pageVersion == null)
            return NotFound();

        if (pageVersion.Deleted)
            return BadRequest("Version is in deleted state");

        var dto = pageVersion.GetDTO();

        try
        {
            await ResolveVersionFullContent(parentPage, dto);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Generating old page content with diffs failed for viewing");
            return Problem("Failed to generate old page content");
        }

        return dto;
    }

    [HttpPost("{id:long}/versions/{version:int}/revertTo")]
    public virtual async Task<ActionResult> RevertResourceVersion([Required] long id,
        [Required] int version)
    {
        IDbContextTransaction? transaction = null;

        if (UsePageUpdateTransaction)
            transaction = await Database.Database.BeginTransactionAsync();

        var page = await Database.VersionedPages.FindAsync(id);

        if (page == null)
            return NotFound();

        var pageDTO = page.GetDTO(-1);

        if (!CanEdit(page, pageDTO, out var editFail))
            return editFail;

        // Load version after checking edit permission
        var pageVersion = await Database.PageVersions.FindAsync(id, version);

        if (pageVersion == null)
            return NotFound();

        if (pageVersion.Deleted)
            return BadRequest("Version is in deleted state");

        var user = HttpContext.AuthenticatedUserOrThrow();

        var versionDTO = pageVersion.GetDTO();

        try
        {
            await ResolveVersionFullContent(page, versionDTO);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Generating old page content with diffs failed for reverting to it");
            return Problem("Failed to generate old page content");
        }

        if (string.IsNullOrWhiteSpace(versionDTO.PageContentAtVersion))
        {
            return BadRequest(
                "Page content is empty at the specified version (or an internal server error caused the version " +
                "text to be be incorrectly determined to be empty)");
        }

        if (versionDTO.PageContentAtVersion == page.LatestContent)
            return Ok("Current text matches old version (not reverted)");

        // Updating page content to revert to the old version
        pageDTO.LatestContent = versionDTO.PageContentAtVersion;

        var previousContent = page.LatestContent;
        var previousComment = page.LastEditComment;
        var previousAuthor = page.LastEditorId;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(page, pageDTO);

        if (!changes)
            return Problem("Changes to the model should have been applied, but didn't");

        page.LastEditorId = user.Id;
        page.LastEditComment = $"Reverted to version {version}";
        page.BumpUpdatedAt();

        var previousVersion = page.CreatePreviousVersion(await page.GetCurrentVersion(Database), previousContent);
        previousVersion.EditedById = previousAuthor;
        previousVersion.EditComment = previousComment;

        await Database.PageVersions.AddAsync(previousVersion);

        await Database.ActionLogEntries.AddAsync(new ActionLogEntry(
            $"Page {page.Id} (\"{page.Title.Truncate()}\") reverted to version: {version}",
            description)
        {
            PerformedById = user.Id,
        });

        await EditNotificationsController.SendEditNotice(Notifications, user, page.Id, true);

        await Database.SaveChangesAsync();

        if (transaction != null)
            await transaction.CommitAsync();

        page.OnEdited(JobClient);

        return Ok();
    }

    [NonAction]
    protected bool ValidatePermalink(string permalink, out ActionResult failure)
    {
        if (permalink.Contains(' '))
        {
            failure = BadRequest("Permalink cannot have spaces");
            return false;
        }

        foreach (var character in permalink)
        {
            switch (character)
            {
                case <= '"':
                case >= '$' and <= ')':
                case ',':
                case '?':
                case '\\':
                case '`':
                case >= (char)127:
                    failure = BadRequest("Permalink cannot have special characters");
                    return false;
            }
        }

        failure = new OkResult();
        return true;
    }

    private bool CanEdit(VersionedPage page, VersionedPageDTO pageDTO, out ActionResult failResult)
    {
        if (page.Type != HandledPageType)
        {
            failResult = NotFound();
            return false;
        }

        if (page.Deleted)
        {
            failResult = BadRequest("Page is in deleted state");
            return false;
        }

        // Changing visibility requires extra permissions
        if (page.Visibility != pageDTO.Visibility)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(PrimaryPublisherGroupType,
                    AuthenticationScopeRestriction.None) &&
                !HttpContext.HasAuthenticatedUserWithGroup(ExtraAccessGroup, AuthenticationScopeRestriction.None))
            {
                failResult = this.WorkingForbid("You lack required permissions to edit visibility");
                return false;
            }
        }

        // Published pages need extra permissions to edit
        if (pageDTO.Visibility != PageVisibility.HiddenDraft)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(PrimaryPublisherGroupType,
                    AuthenticationScopeRestriction.None) &&
                !HttpContext.HasAuthenticatedUserWithGroup(ExtraAccessGroup, AuthenticationScopeRestriction.None))
            {
                failResult = this.WorkingForbid("You lack required permissions to edit this as it is not a draft");
                return false;
            }
        }

        failResult = new OkResult();
        return true;
    }

    [NonAction]
    private async Task ResolveVersionFullContent(VersionedPage page, PageVersionDTO versionDTO)
    {
        var pageContent = page.LatestContent;

        // Apply reverse diffs to go from the latest content up to the specified old version
        var otherOldVersions = await Database.PageVersions.AsNoTracking()
            .Where(v => v.PageId == page.Id && v.Version > versionDTO.Version).OrderByDescending(v => v.Version)
            .ToListAsync();

        // TODO: if this ends up being a performance concern, add either memory or redis caching for like 15 minutes of
        // historical contents of pages (long cache time is fine as historical versions cannot change)

        var stringBuilder = new StringBuilder(pageContent.Length);

        foreach (var oldVersion in otherOldVersions)
        {
            stringBuilder = DiffGenerator.Default.ApplyDiff(pageContent, oldVersion.DecodeDiffData(),
                DiffGenerator.DiffMatchMode.NormalSlightDeviance, stringBuilder);

            // TODO: maybe this could be a bit more efficient if the diff applying could take the old text in as a
            // StringBuilder
            pageContent = stringBuilder.ToString();
        }

        // After processing newer versions than versionDTO, process that last

        stringBuilder = DiffGenerator.Default.ApplyDiff(pageContent, versionDTO.DecodeDiffData(),
            DiffGenerator.DiffMatchMode.NormalSlightDeviance, stringBuilder);

        versionDTO.PageContentAtVersion = stringBuilder.ToString();
    }
}
