namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Shared.Models;
using Shared.Models.Enums;

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
        // TODO: if anything ever gets more than a 500 comments, we will need pagination APIs and GUI
        var rows = await database.TranslationComments.AsNoTracking().Where(comment =>
                comment.ProjectId == projectId && comment.Key == key && comment.LanguageCode == language &&
                !comment.Deleted)
            .OrderBy(comment => comment.CreatedAt).Take(500).ToListAsync();
        return rows.Select(comment => comment.GetDTO()).ToArray();
    }

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
        if (project == null || !await database.TranslationValues.AnyAsync(v => v.ProjectId == request.ProjectId &&
                v.Key == request.Key && v.LanguageCode == request.LanguageCode))
        {
            return NotFound();
        }

        var comment = new TranslationComment
        {
            ProjectId = request.ProjectId, Key = request.Key, LanguageCode = request.LanguageCode,
            AuthorUserId = actor.Id, RawMarkdown = request.RawMarkdown,
        };

        await database.TranslationComments.AddAsync(comment);
        await database.SaveChangesAsync();

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
                TargetPath = $"/translations/{project.Slug}/{request.LanguageCode}/edit" +
                    $"?key={Uri.EscapeDataString(request.Key)}",
            });
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Translation comment {CommentId} added to project {ProjectId}, key {Key}, " +
            "language {LanguageCode} by {Email}", comment.Id, request.ProjectId, request.Key, request.LanguageCode,
            actor.Email);
        return CreatedAtAction(nameof(Comments), new
        {
            request.ProjectId,
            request.Key,
            language = request.LanguageCode,
        }, comment.GetDTO());
    }
}
