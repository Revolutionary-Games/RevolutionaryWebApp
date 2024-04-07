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
using Microsoft.AspNetCore.Mvc;
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
///   Main controller for <see cref="VersionedPage"/> handling
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class PagesController : Controller
{
    private readonly ILogger<PagesController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public PagesController(ILogger<PagesController> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    [HttpGet]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public async Task<PagedResult<VersionedPageInfo>> GetList([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize, bool deleted = false)
    {
        IQueryable<VersionedPage> query;

        try
        {
            query = database.VersionedPages.AsNoTracking().OrderBy(sortColumn, sortDirection)
                .Where(p => p.Deleted == deleted && p.Type == PageType.NormalPage);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
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
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    public async Task<ActionResult<VersionedPageDTO>> GetSingle([Required] long id)
    {
        var page = await database.VersionedPages.FindAsync(id);

        if (page == null || page.Deleted || page.Type != PageType.NormalPage)
            return NotFound();

        var version = await page.GetCurrentVersion(database);

        return page.GetDTO(version);
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    [HttpPost]
    public async Task<ActionResult> CreatePage([Required] [FromBody] VersionedPageDTO pageDTO)
    {
        if (pageDTO.Visibility != PageVisibility.HiddenDraft)
        {
            return BadRequest("Page cannot start in visible mode");
        }

        if (await database.VersionedPages.AsNoTracking().FirstOrDefaultAsync(p =>
                p.Title == pageDTO.Title || (p.Permalink != null && p.Permalink == pageDTO.Permalink)) != null)
        {
            return BadRequest("Page with the given title or permalink already exists");
        }

        var user = HttpContext.AuthenticatedUserOrThrow();

        var page = new VersionedPage(pageDTO.Title)
        {
            Visibility = pageDTO.Visibility,
            Permalink = pageDTO.Permalink,
            Type = PageType.NormalPage,
            CreatorId = user.Id,
            LastEditComment = "Initial version",
        };

        await database.VersionedPages.AddAsync(page);

        await database.ActionLogEntries.AddAsync(new ActionLogEntry($"New page (\"{page.Title.Truncate()}\") created")
        {
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        return Ok();
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    [HttpPut("{id:long}")]
    public async Task<ActionResult> UpdatePage([Required] long id, [Required] [FromBody] VersionedPageDTO pageDTO)
    {
        var page = await database.VersionedPages.FindAsync(id);

        if (page == null || page.Deleted || page.Type != PageType.NormalPage)
            return NotFound();

        if (page.Visibility != pageDTO.Visibility)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(GroupType.SiteLayoutPublisher,
                    AuthenticationScopeRestriction.None) &&
                !HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None))
            {
                return this.WorkingForbid("You lack required permissions to edit visibility");
            }
        }

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
                        new JsonSerializerOptions(JsonSerializerDefaults.General)))
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

        page.OnEdited(jobClient);

        return Ok();
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    [HttpDelete("{id:long}")]
    public async Task<ActionResult> DeleteResource([Required] long id)
    {
        var page = await database.VersionedPages.FindAsync(id);

        if (page == null || page.Type != PageType.NormalPage)
            return NotFound();

        if (page.Deleted)
            return BadRequest("Page is already deleted");

        if (page.Visibility != PageVisibility.HiddenDraft)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(GroupType.SiteLayoutPublisher,
                    AuthenticationScopeRestriction.None) &&
                !HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None))
            {
                return this.WorkingForbid("Published pages can only be deleted by page publishers");
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

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SitePageEditor, AllowAdmin = true)]
    [HttpPost("{id:long}/restore")]
    public async Task<ActionResult> RestoreResource([Required] long id)
    {
        var page = await database.VersionedPages.FindAsync(id);

        if (page == null || page.Type != PageType.NormalPage)
            return NotFound();

        if (!page.Deleted)
            return BadRequest("Page is not deleted deleted");

        if (page.Visibility != PageVisibility.HiddenDraft)
        {
            if (!HttpContext.HasAuthenticatedUserWithGroup(GroupType.SiteLayoutPublisher,
                    AuthenticationScopeRestriction.None) &&
                !HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None))
            {
                return this.WorkingForbid("Published pages can only be restored by page publishers");
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
}
