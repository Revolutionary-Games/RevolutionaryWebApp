namespace RevolutionaryWebApp.Server.Controllers.Pages;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Pages;
using Shared;
using Shared.Models.Enums;
using Shared.Models.Pages;

/// <summary>
///   Main controller for <see cref="VersionedPage"/> handling
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class PagesController : Controller
{
    private readonly ILogger<PagesController> logger;
    private readonly NotificationsEnabledDb database;

    public PagesController(ILogger<PagesController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
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
}
