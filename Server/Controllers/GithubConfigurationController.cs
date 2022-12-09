namespace ThriveDevCenter.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using DevCenterCommunication.Models;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class GithubConfigurationController : Controller
{
    private readonly ILogger<GithubConfigurationController> logger;
    private readonly NotificationsEnabledDb database;

    public GithubConfigurationController(ILogger<GithubConfigurationController> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet("viewSecret")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<GithubWebhookDTO> GetSecret()
    {
        logger.LogInformation("Github webhook secret viewed by {Email}", HttpContext.AuthenticatedUser()!.Email);
        return (await GetOrCreateHook()).GetDTO();
    }

    [HttpPost("recreateSecret")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<GithubWebhookDTO> RecreateSecret()
    {
        var existing = await GetOrCreateHook();

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = "Github webhook secret recreated",
            PerformedById = HttpContext.AuthenticatedUser()!.Id,
        });

        existing.CreateSecret();
        await database.SaveChangesAsync();

        return existing.GetDTO();
    }

    [HttpGet("autoComments")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<GithubAutoCommentDTO>> GetAutoComments([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<GithubAutoComment> query;

        try
        {
            query = database.GithubAutoComments.OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO());
    }

    [HttpPost("autoComments")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> CreateAutoComment([Required] [FromBody] GithubAutoCommentDTO request)
    {
        var comment = new GithubAutoComment
        {
            Enabled = request.Enabled,
            CommentText = request.CommentText,
            Condition = request.Condition,
            Repository = request.Repository,
        };

        var user = HttpContext.AuthenticatedUser()!;

        await database.GithubAutoComments.AddAsync(comment);
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"New Github auto comment created (condition: {comment.Condition})",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("New Github auto comment {Id} created by {Email}", comment.Id, user.Email);

        return Ok();
    }

    [HttpGet("autoComments/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<ActionResult<GithubAutoCommentDTO>> GetAutoComment([Required] long id)
    {
        var result = await database.GithubAutoComments.FindAsync(id);

        if (result == null)
            return NotFound();

        return result.GetDTO();
    }

    [HttpPut("autoComments/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> UpdateAutoComment([Required] [FromBody] GithubAutoCommentDTO request)
    {
        var comment = await database.GithubAutoComments.FindAsync(request.Id);

        if (comment == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        var (changes, description, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(comment, request);

        if (!changes)
            return Ok();

        comment.BumpUpdatedAt();

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Github auto comment {comment.Id} edited",

            // TODO: there could be an extra info property where the description is stored
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Github auto comment {Id} edited by {Email}, changes: {Description}", comment.Id,
            user.Email, description);
        return Ok();
    }

    [HttpDelete("autoComments/{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<IActionResult> DeleteAutoComment([Required] long id)
    {
        var comment = await database.GithubAutoComments.FindAsync(id);

        if (comment == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUser()!;

        database.GithubAutoComments.Remove(comment);
        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = $"Github auto comment {comment.Id} deleted",
            PerformedById = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("Github auto comment {Id} deleted by {Email}", comment.Id, user.Email);
        return Ok();
    }

    [NonAction]
    private async Task<GithubWebhook> GetOrCreateHook()
    {
        var existing = await database.GithubWebhooks.FindAsync(AppInfo.SingleResourceTableRowId);

        if (existing != null)
            return existing;

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = "New Github webhook secret created",
            PerformedById = HttpContext.AuthenticatedUser()!.Id,
        });

        var webhook = new GithubWebhook
        {
            Id = AppInfo.SingleResourceTableRowId,
        };
        webhook.CreateSecret();

        await database.GithubWebhooks.AddAsync(webhook);
        await database.SaveChangesAsync();

        return webhook;
    }
}
