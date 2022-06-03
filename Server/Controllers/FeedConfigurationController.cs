using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class FeedConfigurationController : BaseSoftDeletedResourceController<Feed, FeedInfo, FeedDTO>
{
    private readonly ILogger<FeedConfigurationController> logger;
    private readonly NotificationsEnabledDb database;

    public FeedConfigurationController(ILogger<FeedConfigurationController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    protected override ILogger Logger => logger;
    protected override DbSet<Feed> Entities => database.Feeds;

    protected override UserAccessLevel RequiredViewAccessLevel => UserAccessLevel.Admin;

    [HttpPost]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult> CreateNew([Required] FeedDTO request)
    {
        if (!CheckPollIntervalParameters(request, out var badRequest))
            return badRequest!;

        var feed = new Feed(request.Url, request.Name, request.PollInterval)
        {
            HtmlFeedItemEntryTemplate = request.HtmlFeedItemEntryTemplate,
            HtmlFeedVersionSuffix = request.HtmlFeedVersionSuffix,
            MaxItemLength = request.MaxItemLength,
            MaxItems = request.MaxItems,
        };

        if (request.PreprocessingActions is { Count: > 0 })
            feed.PreprocessingActions = request.PreprocessingActions;

        NormalizeFeedData(feed);

        if (await ConflictsWithExistingNames(feed))
            return BadRequest("Feed name is already in-use");

        var action = new AdminAction()
        {
            Message = $"New Feed created, url: {feed.Url}, name: {feed.Name}",
            PerformedById = HttpContext.AuthenticatedUser()!.Id
        };

        await database.Feeds.AddAsync(feed);
        await database.AdminActions.AddAsync(action);

        await database.SaveChangesAsync();

        return Created($"/admin/feeds/{feed.Id}", feed.GetDTO());
    }

    [HttpPut("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> UpdateFeed([Required] [FromBody] FeedDTO request)
    {
        if (!CheckPollIntervalParameters(request, out var badRequest))
            return badRequest!;

        var item = await FindAndCheckAccess(request.Id);

        if (item == null || item.Deleted)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(item, request);

        if (!changes)
            return Ok();

        NormalizeFeedData(item);

        if (await ConflictsWithExistingNames(item))
            return BadRequest("Feed name is already in-use");

        item.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction()
        {
            Message = $"Feed {item.Id} edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Feed {Id} edited by {Email}, changes: {Description}", item.Id,
            user.Email, description);
        return Ok();
    }

    [NonAction]
    protected override Task SaveResourceChanges(Feed resource)
    {
        resource.BumpUpdatedAt();
        return database.SaveChangesAsync();
    }

    [NonAction]
    private bool CheckPollIntervalParameters(FeedDTO request, out ActionResult? badRequest)
    {
        if (request.PollInterval < TimeSpan.FromMinutes(1))
        {
            badRequest = BadRequest("Minimum poll interval is 1 minute");
            return false;
        }

        if (request.PollInterval > TimeSpan.FromDays(1))
        {
            badRequest = BadRequest("Maximum poll interval is 1 day");
            return false;
        }

        if (request.CacheTime != null && request.CacheTime > TimeSpan.FromHours(1) ||
            request.CacheTime < TimeSpan.FromMinutes(1))
        {
            badRequest = BadRequest("Cache time is not between 1 minute and 1 hour");
            return false;
        }

        badRequest = null;
        return true;
    }

    [NonAction]
    private void NormalizeFeedData(Feed feed)
    {
        if (string.IsNullOrWhiteSpace(feed.HtmlFeedVersionSuffix))
            feed.HtmlFeedVersionSuffix = null;

        if (string.IsNullOrWhiteSpace(feed.HtmlFeedItemEntryTemplate))
            feed.HtmlFeedItemEntryTemplate = null;
    }

    [NonAction]
    private async Task<bool> ConflictsWithExistingNames(Feed feed)
    {
        if (await database.Feeds.Where(f => f.Id != feed.Id).AnyAsync(f => f.Name == feed.Name) ||
            await database.Feeds.Where(f => f.Id != feed.Id && f.HtmlFeedVersionSuffix != null)
                .AnyAsync(f => f.Name + f.HtmlFeedVersionSuffix! == feed.Name) ||
            await database.CombinedFeeds.AnyAsync(c => c.Name == feed.Name))
        {
            return true;
        }

        if (feed.HtmlFeedVersionSuffix != null)
        {
            var suffixed = feed.Name + feed.HtmlFeedVersionSuffix;

            if (await database.Feeds.Where(f => f.Id != feed.Id).AnyAsync(f => f.Name == suffixed) ||
                await database.Feeds.Where(f => f.Id != feed.Id && f.HtmlFeedVersionSuffix != null)
                    .AnyAsync(f => f.Name + f.HtmlFeedVersionSuffix! == suffixed) ||
                await database.CombinedFeeds.AnyAsync(c => c.Name == suffixed))
            {
                return true;
            }
        }

        return false;
    }
}
