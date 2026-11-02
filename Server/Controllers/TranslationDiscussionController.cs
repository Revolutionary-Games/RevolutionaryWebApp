namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class TranslationDiscussionController : Controller
{
    private static readonly Regex MentionPattern = new(@"(?<!\w)@([A-Za-z0-9_-]{2,50})",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

    private readonly ILogger<TranslationDiscussionController> logger;
    private readonly NotificationsEnabledDb database;

    public TranslationDiscussionController(ILogger<TranslationDiscussionController> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet("comments")]
    public async Task<ActionResult<TranslationCommentDTO[]>> Comments(long projectId, string key, string language)
    {
        var sourceLanguage = await database.TranslationProjects.AsNoTracking()
            .Where(project => project.Id == projectId && project.Enabled)
            .Select(project => project.SourceLanguage)
            .FirstOrDefaultAsync();

        // We could check for deleted status here, but it isn't absolutely required that deleted items can't have
        // their comments read
        if (sourceLanguage == null)
            return NotFound();

        // TODO: if anything ever gets more than a 500 comments, we will need pagination APIs and GUI
        var rows = await database.TranslationComments.AsNoTracking().Where(comment =>
                comment.ProjectId == projectId && comment.Key == key &&
                (comment.LanguageCode == language || (language != sourceLanguage &&
                    comment.LanguageCode == sourceLanguage)) &&
                !comment.Deleted)
            .OrderBy(comment => comment.CreatedAt).Take(500).ToListAsync();
        return rows.Select(comment => comment.GetDTO()).ToArray();
    }

    [HttpDelete("comments/{id:long}")]
    public async Task<IActionResult> DeleteComment(long id)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();
        var comment = await database.TranslationComments.FirstOrDefaultAsync(c => c.Id == id && !c.Deleted);
        if (comment == null)
            return NotFound();

        var groups = actor.AccessCachedGroupsOrThrow();
        var moderator = groups.HasAccessLevel(GroupType.Admin) || groups.HasGroup(GroupType.ContentModerator);
        if (!moderator && (!groups.HasAccessLevel(GroupType.User) || comment.AuthorUserId != actor.Id ||
                DateTime.UtcNow > comment.CreatedAt.AddMinutes(15)))
        {
            return this.WorkingForbid("You are not allowed to delete this comment");
        }

        comment.Deleted = true;
        if (comment.AuthorUserId != actor.Id)
        {
            var project = await database.TranslationProjects.AsNoTracking().IgnoreQueryFilters()
                .FirstAsync(p => p.Id == comment.ProjectId);
            await database.UserNotifications.AddAsync(new UserNotification
            {
                UserId = comment.AuthorUserId,
                DeduplicationKey = $"translation-comment:{comment.Id}:deleted",
                Type = UserNotificationType.TranslationCommentDeleted,
                Message = $"A moderator deleted your translation comment in {project.Name} " +
                    $"({comment.LanguageCode}) on {comment.Key}.",
                TargetPath = $"/translations/{project.Slug}/{comment.LanguageCode}/edit" +
                    $"?key={Uri.EscapeDataString(comment.Key)}",
            });

            // Moderators deleting actions are only logged when they deleted someone else's comment
            if (moderator)
            {
                await database.AdminActions.AddAsync(new AdminAction($"Translation comment {id} deleted")
                {
                    PerformedById = actor.Id,
                });
            }
        }
        else
        {
            await database.ActionLogEntries.AddAsync(new ActionLogEntry($"Translation comment {id} deleted by self")
            {
                PerformedById = actor.Id,
            });
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Translation comment {CommentId} deleted by {Email}", id, actor.Email);
        return Ok();
    }

    [CountContribution(100)]
    [HttpPost("comments")]
    public async Task<ActionResult<TranslationCommentDTO>> AddComment(
        [FromBody] CreateTranslationCommentRequest request)
    {
        var actor = HttpContext.AuthenticatedUser();
        if (actor == null)
            return Unauthorized();

        if (!actor.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.User))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.RawMarkdown))
            return BadRequest("Comment text cannot be empty");

        var project = await database.TranslationProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProjectId && p.Enabled);
        if (project == null)
            return NotFound();

        var targetLanguage = request.ForEveryone ? project.SourceLanguage : request.LanguageCode;
        if (!await database.TranslationValues.AnyAsync(v => v.ProjectId == request.ProjectId &&
                v.Key == request.Key && v.LanguageCode == targetLanguage))
        {
            return NotFound();
        }

        var comment = new TranslationComment
        {
            ProjectId = request.ProjectId, Key = request.Key, LanguageCode = targetLanguage,
            AuthorUserId = actor.Id, RawMarkdown = request.RawMarkdown,
        };

        await database.TranslationComments.AddAsync(comment);
        await database.SaveChangesAsync();

        var subscribers = await database.TranslationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.ProjectId == project.Id &&
                subscription.LanguageCode == targetLanguage &&
                subscription.EventType == TranslationSubscriptionEventType.Comment && subscription.UserId != actor.Id)
            .Select(subscription => subscription.UserId).ToListAsync();
        foreach (var subscriber in subscribers)
        {
            database.UserNotifications.Add(new UserNotification
            {
                UserId = subscriber,
                DeduplicationKey = $"translation-comment:{comment.Id}:subscriber:{subscriber}",
                Type = UserNotificationType.TranslationComments,
                Message = $"New comment in {project.Name} ({targetLanguage}) on {request.Key}.",
                TargetPath = $"/translations/{project.Slug}/{targetLanguage}/edit" +
                    $"?key={Uri.EscapeDataString(request.Key)}",
            });
        }

        var names = MentionPattern.Matches(request.RawMarkdown).Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var users = await database.Users.Where(user => names.Contains(user.UserName))
            .Select(user => new { user.Id, user.UserName }).ToListAsync();

        // Max 10 mentions per message
        if (users.Count > 10)
            users = users.Take(10).ToList();

        foreach (var user in users)
        {
            if (user.Id == actor.Id || users.Count(other => other.UserName == user.UserName) != 1)
                continue;

            await database.UserNotifications.AddAsync(new UserNotification
            {
                UserId = user.Id, DeduplicationKey = $"translation-comment:{comment.Id}:mention:{user.Id}",
                Type = UserNotificationType.TranslationMention,
                Message = $"{actor.UserName} mentioned you in a translation comment.",
                TargetPath = $"/translations/{project.Slug}/{targetLanguage}/edit" +
                    $"?key={Uri.EscapeDataString(request.Key)}",
            });
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Translation comment {CommentId} added to project {ProjectId}, key {Key}, " +
            "language {LanguageCode} by {Email}", comment.Id, request.ProjectId, request.Key, targetLanguage,
            actor.Email);
        return CreatedAtAction(nameof(Comments), new
        {
            request.ProjectId,
            request.Key,
            language = targetLanguage,
        }, comment.GetDTO());
    }
}
