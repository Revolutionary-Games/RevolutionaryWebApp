using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
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
        if (request.Deleted)
            return BadRequest("Can't create in deleted state");

        if (!CheckPollIntervalParameters(request, out var badRequest))
            return badRequest!;

        if (string.IsNullOrWhiteSpace(request.HtmlFeedItemEntryTemplate))
            request.HtmlFeedItemEntryTemplate = null;

        if (string.IsNullOrWhiteSpace(request.HtmlFeedVersionSuffix))
            request.HtmlFeedVersionSuffix = null;

        if (request.HtmlFeedVersionSuffix != null)
        {
            if (request.HtmlFeedItemEntryTemplate == null)
                return BadRequest("HTML template is required when HTML suffix is specified");

            if (request.HtmlFeedVersionSuffix.Length < 2)
                return BadRequest("HTML suffix needs to be at least 2 characters");
        }

        var feed = new Feed(request.Url, request.Name, request.PollInterval)
        {
            CacheTime = request.CacheTime,
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

        feed.Id = (await Entities.MaxAsync(f => (long?)f.Id) ?? 0) + 1;

        var action = new AdminAction()
        {
            Message = $"New Feed created, url: {feed.Url}, name: {feed.Name}",
            PerformedById = HttpContext.AuthenticatedUser()!.Id
        };

        await database.Feeds.AddAsync(feed);
        await database.AdminActions.AddAsync(action);

        await database.SaveChangesAsync();

        return Created("/admin/feeds", feed.GetDTO());
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

        // Reset content time to allow the content to regenerate
        item.ContentUpdatedAt = null;

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

    [HttpGet("availableForCombine")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<List<FeedInfo>>> AvailableForCombine()
    {
        var data = await GetEntitiesForJustInfo(false, "Id", SortDirection.Ascending).Take(1000).ToListAsync();

        return data.Select(f => f.GetInfo()).ToList();
    }

    [HttpGet("{id:long}/discordWebhook")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<PagedResult<FeedDiscordWebhookDTO>>> FeedWebhooks([Required] long id,
        [Required] string sortColumn, [Required] SortDirection sortDirection,
        [Required] [Range(1, int.MaxValue)] int page, [Required] [Range(1, 50)] int pageSize)
    {
        var item = await FindAndCheckAccess(id);

        if (item == null)
            return NotFound();

        IQueryable<FeedDiscordWebhook> query;

        try
        {
            query = database.FeedDiscordWebhooks.Where(w => w.FeedId == id).OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            Logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException() { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpPost("{id:long}/discordWebhook")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<FeedDiscordWebhookDTO>> CreateFeedWebhook([Required] long id,
        [Required] [FromBody] FeedDiscordWebhookDTO request)
    {
        var parentFeed = await FindAndCheckAccess(id);

        if (parentFeed == null || parentFeed.Deleted)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.CustomItemFormat))
            request.CustomItemFormat = null;

        var webhook = new FeedDiscordWebhook(parentFeed.Id, request.WebhookUrl)
        {
            CustomItemFormat = request.CustomItemFormat,
        };

        if (await database.FeedDiscordWebhooks.Where(w => w.FeedId == id)
                .FirstOrDefaultAsync(w => w.WebhookUrl == webhook.WebhookUrl) != null)
        {
            return BadRequest("That webhook is already configured for this feed");
        }

        var action = new AdminAction()
        {
            Message = $"New Feed webhook created, feed: {id}, url: {webhook.WebhookUrl}",
            PerformedById = HttpContext.AuthenticatedUserOrThrow().Id
        };

        await database.FeedDiscordWebhooks.AddAsync(webhook);
        await database.AdminActions.AddAsync(action);

        await database.SaveChangesAsync();

        return Created("/admin/feeds", webhook.GetDTO());
    }

    // TODO: update webhook endpoint

    [HttpDelete("{id:long}/discordWebhook/{webhookUrl}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> DeleteFeedWebhook([Required] long id, [Required] [MaxLength(500)] string webhookUrl)
    {
        webhookUrl = Encoding.UTF8.GetString(Convert.FromBase64String(webhookUrl));

        var item = await database.FeedDiscordWebhooks.FirstOrDefaultAsync(w =>
            w.FeedId == id && w.WebhookUrl == webhookUrl);

        if (item == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        var action = new AdminAction()
        {
            Message = $"Feed ({id}) webhook deleted",
            PerformedById = user.Id,
        };

        logger.LogInformation(
            "Feed {Id} webhook to {WebhookUrl} deleted by: {Email}, custom content was: {CustomItemFormat}",
            id, item.WebhookUrl, user.Email, item.CustomItemFormat);

        database.FeedDiscordWebhooks.Remove(item);
        await database.AdminActions.AddAsync(action);

        await database.SaveChangesAsync();

        return Ok();
    }

    [NonAction]
    protected override IQueryable<Feed> GetEntitiesForJustInfo(bool deleted, string sortColumn,
        SortDirection sortDirection)
    {
        return database.Feeds.AsNoTracking().Where(f => f.Deleted == deleted).OrderBy(sortColumn, sortDirection).Select(
            s => new Feed(s.Url, s.Name, s.PollInterval)
            {
                Id = s.Id,
                CacheTime = s.CacheTime,
                ContentUpdatedAt = s.ContentUpdatedAt,
                PreprocessingActionsRaw = s.PreprocessingActionsRaw,
                HtmlFeedVersionSuffix = s.HtmlFeedVersionSuffix,
                HtmlFeedItemEntryTemplate = s.HtmlFeedItemEntryTemplate,
                Deleted = s.Deleted,
            });
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
