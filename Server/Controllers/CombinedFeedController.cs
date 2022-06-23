using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class CombinedFeedController : Controller
{
    private readonly ILogger<CombinedFeedController> logger;
    private readonly NotificationsEnabledDb database;

    public CombinedFeedController(ILogger<CombinedFeedController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<CombinedFeedInfo>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<CombinedFeed> query;

        try
        {
            query = GetEntitiesForJustInfo(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException() { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetInfo());
    }

    [HttpPost()]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> Create([Required] [FromBody] CombinedFeedDTO request)
    {
        if (!CheckPollIntervalParameters(request))
            return BadRequest("Cache time is not between 1 minute and 1 hour");

        // Get the feeds this combines
        var combinedFrom = new List<Feed>();
        var badRequest = await GetFeedsFromIds(request.CombinedFromFeeds, combinedFrom);

        if (badRequest != null)
            return badRequest;

        var feed = new CombinedFeed(request.Name, request.HtmlFeedItemEntryTemplate)
        {
            CombinedFromFeeds = combinedFrom,
            MaxItems = request.MaxItems,
            CacheTime = request.CacheTime,
        };

        if (await ConflictsWithExistingNames(feed))
            return BadRequest("Feed name is already in-use");

        var user = HttpContext.AuthenticatedUser()!;

        await database.CombinedFeeds.AddAsync(feed);
        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"New Combined feed created, name: {feed.Name}, combine count: {feed.CombinedFromFeeds.Count}",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("New combined feed {Id} created by {Email}", feed.Id, user.Email);

        return Ok();
    }

    [HttpGet("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<CombinedFeedDTO>> GetSingle([Required] long id)
    {
        var result = await database.CombinedFeeds.Include(f => f.CombinedFromFeeds)
            .FirstOrDefaultAsync(f => f.Id == id);

        if (result == null)
            return NotFound();

        return result.GetDTO();
    }

    [HttpPut("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> Update([Required] [FromBody] CombinedFeedDTO request)
    {
        if (!CheckPollIntervalParameters(request))
            return BadRequest("Cache time is not between 1 minute and 1 hour");

        var feed = await database.CombinedFeeds.Include(f => f.CombinedFromFeeds)
            .FirstOrDefaultAsync(f => f.Id == request.Id);

        if (feed == null)
            return NotFound();

        var combinedFrom = new List<Feed>();
        var badRequest = await GetFeedsFromIds(request.CombinedFromFeeds, combinedFrom);

        if (badRequest != null)
            return badRequest;

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(feed, request);

        // We need to manually change the combined from list as that can't be made cleanly with the current model
        // update helpers
        if (!combinedFrom.SequenceEqual(feed.CombinedFromFeeds))
        {
            changes = true;
            var newListText = string.Join(", ", combinedFrom.Select(c => $"{c.Name} ({c.Id})"));
            description += $", combined from feeds list changed to: {newListText})";
        }

        if (!changes)
            return Ok();

        if (await ConflictsWithExistingNames(feed))
            return BadRequest("Feed name is already in-use");

        feed.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"Combined feed {feed.Id} edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Combined feed {Id} edited by {Email}, changes: {Description}", feed.Id,
            user.Email, description);
        return Ok();
    }

    [HttpDelete("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> Delete([Required] long id)
    {
        var feed = await database.CombinedFeeds.FindAsync(id);

        if (feed == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        database.CombinedFeeds.Remove(feed);
        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"Combined feed {feed.Id} deleted",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Combined feed {Id} deleted by {Email}", feed.Id, user.Email);
        return Ok();
    }

    [NonAction]
    private async Task<IActionResult?> GetFeedsFromIds(IEnumerable<long> ids, List<Feed> combinedFrom)
    {
        foreach (var id in ids)
        {
            var item = await database.Feeds.FindAsync(id);

            if (item == null || item.Deleted)
            {
                return BadRequest("One or more feeds to combine into this don't exist or are in deleted state");
            }

            combinedFrom.Add(item);
        }

        return null;
    }

    [NonAction]
    private bool CheckPollIntervalParameters(CombinedFeedDTO request)
    {
        if (request.CacheTime > TimeSpan.FromHours(1) || request.CacheTime < TimeSpan.FromMinutes(1))
        {
            return false;
        }

        return true;
    }

    [NonAction]
    private async Task<bool> ConflictsWithExistingNames(CombinedFeed feed)
    {
        if (await database.Feeds.AnyAsync(f => f.Name == feed.Name) ||
            await database.Feeds.Where(f => f.HtmlFeedVersionSuffix != null)
                .AnyAsync(f => f.Name + f.HtmlFeedVersionSuffix! == feed.Name) ||
            await database.CombinedFeeds.Where(f => f.Id != feed.Id).AnyAsync(c => c.Name == feed.Name))
        {
            return true;
        }

        return false;
    }

    [NonAction]
    private IQueryable<CombinedFeed> GetEntitiesForJustInfo(string sortColumn, SortDirection sortDirection)
    {
        // TODO: check if using include here is a good idea as this may need to load a ton of data
        return database.CombinedFeeds.AsNoTracking().Include(f => f.CombinedFromFeeds)
            .OrderBy(sortColumn, sortDirection).Select(
                s => new CombinedFeed(s.Name, s.HtmlFeedItemEntryTemplate)
                {
                    Id = s.Id,
                    CacheTime = s.CacheTime,
                    ContentUpdatedAt = s.ContentUpdatedAt,
                    HtmlFeedItemEntryTemplate = s.HtmlFeedItemEntryTemplate,
                    CombinedFromFeeds = s.CombinedFromFeeds,
                });
    }
}
