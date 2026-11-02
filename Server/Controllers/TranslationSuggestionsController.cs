namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Services;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class TranslationSuggestionsController : Controller
{
    private readonly ILogger<TranslationSuggestionsController> logger;
    private readonly NotificationsEnabledDb database;

    public TranslationSuggestionsController(ILogger<TranslationSuggestionsController> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet]
    public async Task<ActionResult<TranslationSuggestionDTO[]>> List(long projectId, string key, string language,
        CancellationToken cancellationToken)
    {
        // Filter out deleted project's suggestions
        if (!await database.TranslationValues.AnyAsync(value => value.ProjectId == projectId &&
                value.Key == key && value.LanguageCode == language, cancellationToken))
        {
            return NotFound();
        }

        // We assume no text ever gets more than 100 open suggestions at once. Even if something gets 100 suggestions,
        // we probably should just disallow opening more before old ones are closed.
        return await database.TranslationSuggestions.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.Key == key && s.LanguageCode == language &&
                s.Status == TranslationSuggestionStatus.Open)
            .OrderByDescending(s => s.CreatedAt).Take(100)
            .Select(s => new TranslationSuggestionDTO
            {
                Id = s.Id,
                AuthorUserId = s.AuthorUserId,
                Text = s.Text,
                Score = s.Score,
                Status = s.Status,
                CreatedAt = s.CreatedAt,
            })
            .ToArrayAsync(cancellationToken);
    }

    [CountContribution(30)]
    [HttpPost]
    [RequireTranslatorAgreement]
    public async Task<ActionResult<TranslationSuggestionDTO>> Create(
        [FromBody] CreateTranslationSuggestionRequest request)
    {
        var actor = HttpContext.AuthenticatedUser();
        if (actor == null)
            return Unauthorized();
        if (!actor.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.User))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest("Suggestion cannot be empty");

        await using var projectLock = await TranslationProjectAdvisoryLock.AcquireAsync(database, request.ProjectId,
            HttpContext.RequestAborted);

        var value = await database.TranslationValues.AsNoTracking().FirstOrDefaultAsync(v =>
            v.ProjectId == request.ProjectId && v.Key == request.Key && v.LanguageCode == request.LanguageCode &&
            v.Unit.Active);
        if (value == null)
            return NotFound();

        if (await database.TranslationSuggestions.AnyAsync(s => s.ProjectId == request.ProjectId &&
                    s.Key == request.Key && s.LanguageCode == request.LanguageCode && s.Text == request.Text,
                HttpContext.RequestAborted))
        {
            return Conflict("This translation has already been suggested for this key");
        }

        var suggestion = new TranslationSuggestion
        {
            ProjectId = request.ProjectId, Key = request.Key, LanguageCode = request.LanguageCode,
            AuthorUserId = actor.Id, Text = request.Text,
            SourceRevision = value.SourceRevisionTranslatedFrom,
        };
        await database.TranslationSuggestions.AddAsync(suggestion);
        await database.SaveChangesAsync();

        var project = await database.TranslationProjects.AsNoTracking()
            .FirstAsync(project => project.Id == request.ProjectId);
        var subscribers = await database.TranslationSubscriptions.AsNoTracking()
            .Where(subscription => subscription.ProjectId == request.ProjectId &&
                subscription.LanguageCode == request.LanguageCode &&
                subscription.EventType == TranslationSubscriptionEventType.Suggestion &&
                subscription.UserId != actor.Id)
            .Select(subscription => subscription.UserId).ToListAsync();
        foreach (var subscriber in subscribers)
        {
            database.UserNotifications.Add(new UserNotification
            {
                UserId = subscriber,
                DeduplicationKey = $"translation-suggestion:{suggestion.Id}:subscriber:{subscriber}",
                Type = UserNotificationType.TranslationSuggestionCreated,
                Message = $"New suggestion in {project.Name} ({request.LanguageCode}) on {request.Key}.",
                TargetPath = $"/translations/{project.Slug}/{request.LanguageCode}/edit" +
                    $"?key={Uri.EscapeDataString(request.Key)}",
            });
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Translation suggestion {SuggestionId} created for project {ProjectId}, key {Key}, " +
            "language {LanguageCode} by {Email}", suggestion.Id, request.ProjectId, request.Key,
            request.LanguageCode, actor.Email);
        return CreatedAtAction(nameof(List), new
        {
            request.ProjectId,
            request.Key,
            language = request.LanguageCode,
        }, suggestion.GetDTO());
    }

    [CountContribution(1)]
    [HttpPut("{id:long}/vote")]
    public async Task<IActionResult> Vote(long id, [FromBody] VoteOnTranslationSuggestionRequest request)
    {
        var actor = HttpContext.AuthenticatedUser();
        if (actor == null)
            return Unauthorized();
        if (!actor.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.User))
            return Forbid();
        if (request.Value is < -1 or > 1)
            return BadRequest("Vote must be -1, 0, or +1");

        var projectId = await database.TranslationSuggestions.AsNoTracking().Where(s => s.Id == id)
            .Select(s => (long?)s.ProjectId).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (projectId == null)
            return NotFound();

        // This lock is mostly here to be consistent with Accept which needs to actually lock to make changes
        await using var projectLock = await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId.Value,
            HttpContext.RequestAborted);
        var suggestion = await database.TranslationSuggestions.FirstOrDefaultAsync(item => item.Id == id);
        if (suggestion == null || suggestion.Status != TranslationSuggestionStatus.Open)
            return NotFound();
        if (suggestion.AuthorUserId == actor.Id)
            return BadRequest("Cannot vote on your own suggestion");

        var existing = await database.TranslationSuggestionVotes.FindAsync(id, actor.Id);

        // Full refresh the vote count. If this is too slow, we couldn't just increment it
        suggestion.Score = (await database.TranslationSuggestionVotes.Where(vote => vote.SuggestionId == id)
            .SumAsync(vote => (int?)vote.Value) ?? 0) + request.Value - (existing?.Value ?? 0);

        if (request.Value == 0)
        {
            if (existing != null)
            {
                database.TranslationSuggestionVotes.Remove(existing);
            }
        }
        else if (existing == null)
        {
            await database.TranslationSuggestionVotes.AddAsync(new TranslationSuggestionVote
            {
                SuggestionId = id, UserId = actor.Id, Value = request.Value, Suggestion = suggestion,
            });
        }
        else
        {
            existing.Value = request.Value;

            // This is probably not needed?
            existing.Suggestion = suggestion;
        }

        await database.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        var actor = HttpContext.AuthenticatedUser();
        if (actor == null)
            return Unauthorized();

        var groups = actor.AccessCachedGroupsOrThrow();
        if (!groups.HasAccessLevel(GroupType.Admin) && !groups.HasGroup(GroupType.ContentModerator))
            return this.WorkingForbid("You don't have permission to delete translation suggestions");

        var projectId = await database.TranslationSuggestions.IgnoreQueryFilters().AsNoTracking().Where(s => s.Id == id)
            .Select(s => (long?)s.ProjectId).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (projectId == null)
            return NotFound();

        await using var projectLock = await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId.Value,
            HttpContext.RequestAborted);
        var suggestion = await database.TranslationSuggestions.FirstOrDefaultAsync(s => s.Id == id);
        if (suggestion == null)
            return NotFound();

        var project = await database.TranslationProjects.AsNoTracking()
            .FirstAsync(p => p.Id == suggestion.ProjectId);
        database.TranslationSuggestions.Remove(suggestion);
        await database.UserNotifications.AddAsync(new UserNotification
        {
            UserId = suggestion.AuthorUserId,
            DeduplicationKey = $"translation-suggestion:{id}:deleted",
            Type = UserNotificationType.TranslationSuggestionDeleted,
            Message = $"A moderator deleted your translation suggestion in {project.Name} " +
                $"({suggestion.LanguageCode}) on {suggestion.Key}.",
            TargetPath = $"/translations/{project.Slug}/{suggestion.LanguageCode}/edit" +
                $"?key={Uri.EscapeDataString(suggestion.Key)}",
        });
        await database.AdminActions.AddAsync(new AdminAction($"Translation suggestion {id} deleted")
        {
            PerformedById = actor.Id,
        });

        await database.SaveChangesAsync();
        logger.LogInformation("Translation suggestion {SuggestionId} deleted by {Email}", id, actor.Email);
        return NoContent();
    }

    [CountContribution(10)]
    [HttpPost("{id:long}/accept")]
    [RequireTranslatorAgreement]
    public async Task<IActionResult> Accept(long id)
    {
        var actor = HttpContext.AuthenticatedUser();
        if (actor == null)
            return Unauthorized();
        if (!actor.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.User))
            return Forbid();

        var projectId = await database.TranslationSuggestions.AsNoTracking().Where(s => s.Id == id)
            .Select(s => (long?)s.ProjectId).FirstOrDefaultAsync(HttpContext.RequestAborted);
        if (projectId == null)
            return NotFound();

        await using var projectLock = await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId.Value,
            HttpContext.RequestAborted);
        var suggestion = await database.TranslationSuggestions.FirstOrDefaultAsync(item => item.Id == id);
        if (suggestion == null || suggestion.Status != TranslationSuggestionStatus.Open)
            return NotFound();
        if (suggestion.Score < 0)
            return BadRequest("Cannot accept a suggestion with a negative score");

        var value = await database.TranslationValues.FirstOrDefaultAsync(item =>
            item.ProjectId == suggestion.ProjectId && item.Key == suggestion.Key &&
            item.LanguageCode == suggestion.LanguageCode);
        var unit = await database.TranslationUnits.FirstOrDefaultAsync(item =>
            item.ProjectId == suggestion.ProjectId && item.Key == suggestion.Key);
        var project = await database.TranslationProjects.FirstOrDefaultAsync(item => item.Id == suggestion.ProjectId);
        if (value == null || unit == null || project == null ||
            suggestion.SourceRevision != unit.SourceRevision)
        {
            return Conflict("Suggestion was made for an older source text");
        }

        if (!unit.Active)
            return Conflict("This source text has been removed and does not need translation");

        if (!TranslationPermissions.CanTranslate(actor.AccessCachedGroupsOrThrow(),
                suggestion.LanguageCode == project.SourceLanguage))
        {
            return this.WorkingForbid("You don't have direct translation permissions");
        }

        var previousText = value.Text;
        var previousState = value.State;
        var previousEditor = value.LastEditedByUserId;
        suggestion.Status = TranslationSuggestionStatus.Accepted;
        suggestion.ResolvedAt = DateTime.UtcNow;
        suggestion.ResolvedByUserId = actor.Id;
        if (previousText != suggestion.Text)
        {
            value.Text = suggestion.Text;
            value.State = TranslationState.Translated;
            value.Version = Guid.NewGuid();
            value.LastEditedByUserId = suggestion.AuthorUserId;
            value.LastEditedAt = DateTime.UtcNow;
            value.SourceRevisionTranslatedFrom = unit.SourceRevision;
            value.SourceTextTranslatedFrom = unit.SourceText;
            if (suggestion.LanguageCode == project.SourceLanguage)
            {
                unit.SourceText = suggestion.Text;
                if (project.Format == TranslationFormat.Manual)
                {
                    unit.SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(suggestion.Text)));
                    value.LastImportedText = suggestion.Text;
                }

                ++unit.SourceRevision;
                unit.SourceUpdatedAt = DateTime.UtcNow;
                value.SourceRevisionTranslatedFrom = unit.SourceRevision;
                value.SourceTextTranslatedFrom = suggestion.Text;
                await TranslationSourceChangeHelper.MarkTargetsOutdated(database, project, unit,
                    HttpContext.RequestAborted);
            }

            var revision = ++project.CurrentRevision;
            if (project.Format != TranslationFormat.Manual)
            {
                value.PendingRevision = revision;
                value.PendingSince ??= DateTime.UtcNow;
            }

            await database.TranslationChanges.AddAsync(new TranslationChange
            {
                ProjectId = project.Id, Key = suggestion.Key, LanguageCode = suggestion.LanguageCode,
                ActorUserId = actor.Id, ContributorUserId = suggestion.AuthorUserId,
                Kind = TranslationChangeKind.SuggestionAccepted,
                OldText = previousText, NewText = suggestion.Text,
                OldState = previousState, NewState = value.State,
                SourceRevision = unit.SourceRevision, ProjectRevision = revision,
                CountsAsContribution = true,
            });

            if (previousEditor != null && previousEditor != actor.Id &&
                previousEditor != suggestion.AuthorUserId)
            {
                await database.UserNotifications.AddAsync(new UserNotification
                {
                    UserId = previousEditor.Value,
                    DeduplicationKey = $"translation-suggestion:{id}:replaced-editor",
                    Type = UserNotificationType.TranslationEdited,
                    Message = $"An accepted suggestion changed your {suggestion.LanguageCode} " +
                        $"translation for {suggestion.Key}.",
                    TargetPath = $"/translations/{project.Slug}/{suggestion.LanguageCode}/edit" +
                        $"?key={Uri.EscapeDataString(suggestion.Key)}",
                });
            }
        }

        if (suggestion.AuthorUserId != actor.Id)
        {
            await database.UserNotifications.AddAsync(new UserNotification
            {
                UserId = suggestion.AuthorUserId,
                DeduplicationKey = $"translation-suggestion:{id}:accepted",
                Type = UserNotificationType.SuggestionAccepted,
                Message = $"Your suggestion for {suggestion.Key} was accepted.",
                TargetPath = $"/translations/{project.Slug}/{suggestion.LanguageCode}/edit" +
                    $"?key={Uri.EscapeDataString(suggestion.Key)}",
            });
        }

        await database.SaveChangesAsync();

        logger.LogInformation("Translation suggestion {SuggestionId} accepted by {Email}", id, actor.Email);
        return NoContent();
    }
}
