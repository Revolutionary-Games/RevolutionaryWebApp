namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using DiffPlex;
using Filters;
using Hangfire;
using Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
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
    protected readonly NotificationsEnabledDb database;
    protected readonly IBackgroundJobClient jobClient;
    protected readonly IHubContext<NotificationsHub, INotifications> notifications;

    public BasePageController(NotificationsEnabledDb database, IBackgroundJobClient jobClient,
        IHubContext<NotificationsHub, INotifications> notifications)
    {
        this.database = database;
        this.jobClient = jobClient;
        this.notifications = notifications;
    }

    protected abstract ILogger Logger { get; }
    protected abstract PageType HandledPageType { get; }

    protected abstract GroupType PrimaryPublisherGroupType { get; }
    protected abstract GroupType ExtraAccessGroup { get; }

    /// <summary>
    ///   Get List of pages. Should be overridden in base classes, otherwise this endpoint is exposed without
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
            query = database.VersionedPages.AsNoTracking().OrderBy(sortColumn, sortDirection)
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
        var page = await database.VersionedPages.FindAsync(id);

        if (page == null || page.Type != HandledPageType)
            return NotFound();

        var version = await page.GetCurrentVersion(database);

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

        if (await database.VersionedPages.AsNoTracking().FirstOrDefaultAsync(p =>
                p.Title == pageDTO.Title || (p.Permalink != null && p.Permalink == pageDTO.Permalink)) != null)
        {
            return BadRequest("Page with the given title or permalink already exists");
        }

        if (pageDTO.Permalink != null && !ValidatePermalink(pageDTO.Permalink, out var fail))
            return fail;

        var page = new VersionedPage(pageDTO.Title)
        {
            Visibility = pageDTO.Visibility,
            Permalink = pageDTO.Permalink,
            Type = HandledPageType,
            CreatorId = user.Id,
            LastEditComment = "Initial version",
            LastEditorId = user.Id,
        };

        await database.VersionedPages.AddAsync(page);

        await database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"New page (\"{page.Title.Truncate()}\" type: {page.Type}) created")
            {
                PerformedById = user.Id,
                Extended = $"Title: {page.Title}, permalink: {page.Permalink}",
            });

        await database.SaveChangesAsync();

        return Ok(page.Id.ToString());
    }

    [HttpPut("{id:long}")]
    public virtual async Task<ActionResult> UpdatePage([Required] long id,
        [Required] [FromBody] VersionedPageDTO pageDTO)
    {
        // Maybe a transaction here helps against unintended duplicate edits
        var transaction = await database.Database.BeginTransactionAsync();
        var page = await database.VersionedPages.FindAsync(id);

        if (page == null || page.Deleted || page.Type != HandledPageType)
            return NotFound();

        if (page.Visibility != pageDTO.Visibility)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(PrimaryPublisherGroupType,
                    AuthenticationScopeRestriction.None) &&
                !HttpContext.HasAuthenticatedUserWithGroup(ExtraAccessGroup, AuthenticationScopeRestriction.None))
            {
                return this.WorkingForbid("You lack required permissions to edit visibility");
            }
        }

        if (pageDTO.Visibility != PageVisibility.HiddenDraft)
        {
            // Needs to have a permalink when a page is visible
            if (string.IsNullOrWhiteSpace(pageDTO.Permalink))
            {
                // Reuse existing permalink if there is one
                if (string.IsNullOrWhiteSpace(page.Permalink))
                {
                    page.Permalink = VersionedPageDTO.GeneratePermalinkFromTitle(pageDTO.Title);
                }

                pageDTO.Permalink = page.Permalink;
            }

            // Published at is set the first time page is saved when visible
            page.PublishedAt ??= DateTime.UtcNow;
        }
        else if (string.IsNullOrWhiteSpace(pageDTO.Permalink))
        {
            // Don't override permalink if it is set already
            pageDTO.Permalink = page.Permalink;
        }

        if (pageDTO.Permalink != null && !ValidatePermalink(pageDTO.Permalink, out var fail))
            return fail;

        if (await database.VersionedPages.AsNoTracking().FirstOrDefaultAsync(p => p.Id != page.Id &&
                (p.Title == pageDTO.Title || (p.Permalink != null && p.Permalink == pageDTO.Permalink))) != null)
        {
            return BadRequest("Page with the given updated title or permalink already exists");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        if (pageDTO.LatestContent != page.LatestContent)
        {
            // Updating page content
            int version = await page.GetCurrentVersion(database);

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
                // Create edit history from the previous version
                var diff = Differ.Instance.CreateCharacterDiffs(page.LatestContent, previousContent, false);

                var edit = new PageVersion(page, version,
                    JsonSerializer.Serialize(new DiffData(diff),
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                {
                    EditedById = previousAuthor,
                    EditComment = previousComment,
                };

                await database.PageVersions.AddAsync(edit);

                ++version;

                await database.ActionLogEntries.AddAsync(new ActionLogEntry(
                    $"Page {page.Id} (\"{page.Title.Truncate()}\") edited, version is now: {version}",
                    description)
                {
                    PerformedById = user.Id,
                });
            }
            else
            {
                // This is the initial version / replaces the previous version as it was empty
                await database.ActionLogEntries.AddAsync(new ActionLogEntry(
                    $"Page {page.Id} (\"{page.Title.Truncate()}\") edited, last version was blank, " +
                    $"version is now: {version}", description)
                {
                    PerformedById = user.Id,
                });
            }

            await EditNotificationsController.SendEditNotice(notifications, user, page.Id, true);
        }
        else
        {
            // Updating just page properties
            var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(page, pageDTO);

            if (!changes)
                return Ok("No changes");

            page.BumpUpdatedAt();

            await database.ActionLogEntries.AddAsync(
                new ActionLogEntry($"Page {page.Id} (\"{page.Title.Truncate()}\") properties updated", description)
                {
                    PerformedById = user.Id,
                });
        }

        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        page.OnEdited(jobClient);

        return Ok();
    }

    [HttpDelete("{id:long}")]
    public virtual async Task<ActionResult> DeleteResource([Required] long id)
    {
        var page = await database.VersionedPages.FindAsync(id);

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

        await database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"Page {page.Id} (\"{page.Title.Truncate()}\") deleted")
            {
                PerformedById = HttpContext.AuthenticatedUserOrThrow().Id,
            });

        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("{id:long}/restore")]
    public virtual async Task<ActionResult> RestoreResource([Required] long id)
    {
        var page = await database.VersionedPages.FindAsync(id);

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

        await database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"Page {page.Id} (\"{page.Title.Truncate()}\") restored")
            {
                PerformedById = HttpContext.AuthenticatedUserOrThrow().Id,
            });

        await database.SaveChangesAsync();

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
}
