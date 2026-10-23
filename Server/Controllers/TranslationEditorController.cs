namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
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
public class TranslationEditorController : Controller
{
    private readonly ILogger<TranslationEditorController> logger;
    private readonly NotificationsEnabledDb database;

    public TranslationEditorController(ILogger<TranslationEditorController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet("list")]
    public async Task<ActionResult<TranslationListItemDTO[]>> List([FromQuery] long projectId,
        [FromQuery] string language, [FromQuery] string? query = null, [FromQuery] string? state = null,
        [FromQuery] int offset = 0, CancellationToken cancellationToken = default)
    {
        if (offset < 0)
            return BadRequest("Offset must be non-negative");

        var project = await database.TranslationProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.Enabled, cancellationToken);
        if (project == null)
            return NotFound();

        var values = database.TranslationUnits.AsNoTracking()
            .Join(database.TranslationValues.AsNoTracking(), unit => new { unit.ProjectId, unit.Key }, value => new
            {
                value.ProjectId,
                value.Key,
            }, (unit, value) => new { unit, value })
            .Where(t => t.unit.ProjectId == projectId && t.unit.Active && t.value.LanguageCode == language)
            .Select(t => new { t.unit, t.value });

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.Trim().ToLowerInvariant();
            values = values.Where(item => item.unit.Key.ToLower().Contains(term) ||
                item.unit.SourceText.ToLower().Contains(term) || item.value.Text.ToLower().Contains(term));
        }

        if (Enum.TryParse<TranslationState>(state, true, out var parsedState))
        {
            values = values.Where(item => item.value.State == parsedState);
        }
        else if (!string.IsNullOrEmpty(state))
        {
            return BadRequest("Invalid state format");
        }

        return await values.OrderBy(item => item.unit.Key).Skip(offset).Take(50)
            .Select(item => new TranslationListItemDTO
            {
                Key = item.unit.Key,
                SourceText = item.unit.SourceText,
                Text = item.value.Text,
                State = item.value.State,
            }).ToArrayAsync(cancellationToken);
    }

    [HttpGet("neighbor")]
    public async Task<ActionResult<string>> Neighbor([FromQuery] long projectId, [FromQuery] string language,
        [FromQuery] string key, [FromQuery] int direction)
    {
        if (direction is not (-1 or 1))
            return BadRequest("Direction must be -1 or 1");

        var keys = database.TranslationUnits.AsNoTracking()
            .Join(database.TranslationValues.AsNoTracking(), unit => new { unit.ProjectId, unit.Key }, value => new
            {
                value.ProjectId,
                value.Key,
            }, (unit, value) => new { unit, value })
            .Where(t => t.unit.ProjectId == projectId && t.unit.Active && t.value.LanguageCode == language)
            .Select(t => t.unit.Key);

        var neighbor = direction > 0 ?
            await keys.Where(candidate => string.Compare(candidate, key) > 0)
                .OrderBy(candidate => candidate).FirstOrDefaultAsync() :
            await keys.Where(candidate => string.Compare(candidate, key) < 0)
                .OrderByDescending(candidate => candidate).FirstOrDefaultAsync();
        return neighbor == null ? NotFound() : neighbor;
    }

    [HttpGet("history")]
    public async Task<ActionResult<TranslationHistoryDTO[]>> History(long projectId, string key, string language,
        [FromQuery] int offset = 0)
    {
        if (offset < 0)
            return BadRequest("Offset must be non-negative");

        var rows = await database.TranslationChanges.AsNoTracking().Where(change =>
                change.ProjectId == projectId && change.Key == key && change.LanguageCode == language)
            .OrderByDescending(change => change.CreatedAt).ThenByDescending(change => change.Id)
            .Skip(offset).Take(50).ToListAsync();
        return rows.Select(change => change.GetHistoryDTO()).ToArray();
    }

    [HttpGet("nearby")]
    public async Task<ActionResult<NearbyTranslationDTO[]>> Nearby(long projectId, string key, string language)
    {
        return await database.TranslationValues.AsNoTracking().Where(value =>
                value.ProjectId == projectId && value.Key == key && value.LanguageCode != language &&
                value.Text != string.Empty)
            .OrderBy(value => value.LanguageCode).Take(100)
            .Select(value => new NearbyTranslationDTO
            {
                LanguageCode = value.LanguageCode,
                Text = value.Text,
                State = value.State,
            })
            .ToArrayAsync();
    }

    [HttpGet]
    public async Task<ActionResult<TranslationEditorDTO>> Get([FromQuery] long projectId, [FromQuery] string key,
        [FromQuery] string language, CancellationToken cancellationToken)
    {
        var project = await database.TranslationProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.Enabled, cancellationToken);
        if (project == null)
            return NotFound();

        var unit = await database.TranslationUnits.AsNoTracking()
            .FirstOrDefaultAsync(u => u.ProjectId == projectId && u.Key == key && u.Active, cancellationToken);
        var value = await database.TranslationValues.AsNoTracking()
            .FirstOrDefaultAsync(v => v.ProjectId == projectId && v.Key == key && v.LanguageCode == language,
                cancellationToken);
        if (unit == null || value == null)
            return NotFound();

        var groups = HttpContext.AuthenticatedUser()?.AccessCachedGroupsOrThrow();
        var canVerify = groups != null && TranslationPermissions.CanVerify(groups);
        var comments = await database.TranslationComments.CountAsync(c => c.ProjectId == projectId &&
            c.Key == key && c.LanguageCode == language && !c.Deleted, cancellationToken);
        var suggestions = await database.TranslationSuggestions.CountAsync(s => s.ProjectId == projectId &&
                s.Key == key && s.LanguageCode == language && s.Status == TranslationSuggestionStatus.Open,
            cancellationToken);

        return new TranslationEditorDTO
        {
            ProjectId = projectId,
            ProjectSlug = project.Slug,
            Key = key,
            LanguageCode = language,
            SourceText = unit.SourceText,
            SourceTextTranslatedFrom = value.SourceTextTranslatedFrom,
            Text = value.Text,
            State = value.State,
            Version = value.Version,
            Description = unit.Description,
            SourceLocations = unit.SourceLocations,
            CanVerify = canVerify,
            CommentCount = comments,
            SuggestionCount = suggestions,
        };
    }

    [HttpPut]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<ActionResult<TranslationEditorDTO>> Save([FromBody] TranslationSaveRequest request)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();

        // Ensure no concurrent modifications in the project as source edits can cause cascades which would be very
        // tricky otherwise
        await using var projectLock = await TranslationProjectAdvisoryLock.AcquireAsync(database, request.ProjectId,
            HttpContext.RequestAborted);

        var project = await database.TranslationProjects.FirstOrDefaultAsync(p => p.Id == request.ProjectId &&
            p.Enabled);
        if (project == null)
            return NotFound();

        var groups = actor.AccessCachedGroupsOrThrow();
        if (!TranslationPermissions.CanTranslate(groups, request.LanguageCode == project.SourceLanguage))
            return this.WorkingForbid("You don't have permission to translate this");

        var state = request.State;
        if (!Enum.IsDefined(state))
            return BadRequest("Invalid translation state");

        if (state == TranslationState.Verified && !TranslationPermissions.CanVerify(groups))
            return this.WorkingForbid("You don't have permission to verify translations");

        if (string.IsNullOrWhiteSpace(request.Text) && state != TranslationState.NeedsTranslation)
            return BadRequest("Marking a translation 'done' requires text in the translation");

        if (project.Format == TranslationFormat.Manual && request.LanguageCode == project.SourceLanguage &&
            string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest("Manual project source text cannot be empty");
        }

        if (request.Text.Length > 20000)
            return BadRequest("Translation is too long");

        var unit = await database.TranslationUnits.FirstOrDefaultAsync(u => u.ProjectId == request.ProjectId &&
            u.Key == request.Key && u.Active);
        var value = await database.TranslationValues.FirstOrDefaultAsync(v => v.ProjectId == request.ProjectId &&
            v.Key == request.Key && v.LanguageCode == request.LanguageCode);
        if (unit == null || value == null)
            return NotFound();

        if (request.Version != value.Version)
        {
            return Conflict(
                "Translation changed since this page was loaded. Please copy your work and then reload the page.");
        }

        var oldText = value.Text;
        var oldState = value.State;
        var previousEditor = value.LastEditedByUserId;
        var textChanged = oldText != request.Text;
        if (!textChanged && oldState == state)
            return await Get(request.ProjectId, request.Key, request.LanguageCode, HttpContext.RequestAborted);

        value.Text = request.Text;
        value.State = state;
        value.Version = Guid.NewGuid();
        if (textChanged)
        {
            value.LastEditedByUserId = actor.Id;
            value.LastEditedAt = DateTime.UtcNow;
            value.SourceRevisionTranslatedFrom = unit.SourceRevision;
            value.SourceTextTranslatedFrom = unit.SourceText;
        }

        // Need to cascade if source text is updated
        if (textChanged && project.Format == TranslationFormat.Manual &&
            request.LanguageCode == project.SourceLanguage)
        {
            unit.SourceText = request.Text;
            unit.SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(request.Text)));
            ++unit.SourceRevision;
            unit.SourceUpdatedAt = DateTime.UtcNow;
            value.SourceRevisionTranslatedFrom = unit.SourceRevision;
            value.SourceTextTranslatedFrom = request.Text;
            var targets = await database.TranslationValues.Where(other => other.ProjectId == project.Id &&
                other.Key == unit.Key && other.LanguageCode != project.SourceLanguage &&
                other.State != TranslationState.NeedsTranslation).ToListAsync();
            foreach (var target in targets)
            {
                var priorState = target.State;
                target.State = TranslationState.NeedsTranslation;
                target.Version = Guid.NewGuid();
                await database.TranslationChanges.AddAsync(new TranslationChange
                {
                    ProjectId = project.Id, Key = unit.Key, LanguageCode = target.LanguageCode,
                    Kind = TranslationChangeKind.SourceChanged, OldText = target.Text, NewText = target.Text,
                    OldState = priorState, NewState = target.State,
                    SourceRevision = unit.SourceRevision, ProjectRevision = ++project.CurrentRevision,
                });
            }
        }

        var revision = ++project.CurrentRevision;
        if (project.Format != TranslationFormat.Manual)
        {
            value.PendingRevision = revision;
            value.PendingSince ??= DateTime.UtcNow;
        }

        // TODO: should we consider updating the text but not marking it done as a contribution?
        await database.TranslationChanges.AddAsync(new TranslationChange
        {
            ProjectId = project.Id, Key = unit.Key, LanguageCode = value.LanguageCode,
            ActorUserId = actor.Id, Kind = textChanged ?
                TranslationChangeKind.UserEdit :
                TranslationChangeKind.StateChanged,
            ContributorUserId = textChanged && state != TranslationState.NeedsTranslation ? actor.Id : null,
            OldText = oldText, NewText = value.Text, OldState = oldState, NewState = state,
            SourceRevision = unit.SourceRevision, ProjectRevision = project.CurrentRevision,
            CountsAsContribution = textChanged && !string.IsNullOrWhiteSpace(request.Text) &&
                state != TranslationState.NeedsTranslation,
        });

        if (textChanged && previousEditor != null && previousEditor != actor.Id)
        {
            var dedupKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"edited:{project.Id}:{unit.Key}:{value.LanguageCode}:{project.CurrentRevision}")));
            await database.UserNotifications.AddAsync(new UserNotification
            {
                UserId = previousEditor.Value, DeduplicationKey = dedupKey,
                Type = UserNotificationType.TranslationEdited,
                Message = $"Someone edited your {value.LanguageCode} translation for {unit.Key}.",
                TargetPath = $"/translations/{project.Slug}/{value.LanguageCode}/edit" +
                    $"?key={Uri.EscapeDataString(unit.Key)}",
            });
        }

        if (textChanged)
        {
            var subscribers = await database.TranslationSubscriptions.AsNoTracking().Where(s =>
                    s.ProjectId == project.Id && s.LanguageCode == value.LanguageCode &&
                    s.EventType == TranslationSubscriptionEventType.TranslationUpdate && s.UserId != actor.Id &&
                    s.UserId != previousEditor)
                .Select(s => s.UserId).ToListAsync();
            foreach (var subscriber in subscribers)
            {
                await database.UserNotifications.AddAsync(new UserNotification
                {
                    UserId = subscriber,
                    DeduplicationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                        $"translation-update:{project.Id}:{unit.Key}:{value.LanguageCode}:" +
                        project.CurrentRevision))),
                    Type = UserNotificationType.TranslationUpdated,
                    Message = $"A {value.LanguageCode} translation for {unit.Key} was updated.",
                    TargetPath = $"/translations/{project.Slug}/{value.LanguageCode}/edit" +
                        $"?key={Uri.EscapeDataString(unit.Key)}",
                });
            }
        }

        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Translation changed since this page was loaded");
        }

        logger.LogInformation("Translation for project {ProjectSlug}, key {Key}, language {LanguageCode} " +
            "saved by {Email}; text changed: {TextChanged}, state: {OldState} to {NewState}", project.Slug,
            unit.Key, value.LanguageCode, actor.Email, textChanged, oldState, state);

        // This get data is safe to abort as we have saved the changes already, and just are returning data to the user
        return await Get(request.ProjectId, request.Key, request.LanguageCode, HttpContext.RequestAborted);
    }
}
