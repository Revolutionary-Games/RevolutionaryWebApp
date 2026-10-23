namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Shared.Models;

[ApiController]
[Route("api/v1/[controller]")]
public sealed class ManualTranslationKeysController : Controller
{
    private readonly ILogger<ManualTranslationKeysController> logger;
    private readonly ApplicationDbContext database;

    public ManualTranslationKeysController(ILogger<ManualTranslationKeysController> logger,
        ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpPost("{projectId:long}")]
    public async Task<IActionResult> Create(long projectId, [FromBody] CreateManualTranslationKeyRequest request)
    {
        var actor = HttpContext.AuthenticatedUser();
        if (actor == null)
            return Unauthorized();
        if (!TranslationPermissions.CanTranslate(actor.AccessCachedGroupsOrThrow(), true))
            return Forbid();

        var project = await database.TranslationProjects.FirstOrDefaultAsync(p => p.Id == projectId &&
            p.Enabled && p.Format == TranslationFormat.Manual);
        if (project == null)
            return NotFound();

        var key = request.Key.Trim();
        var source = request.SourceText.Trim();
        if (key.Length == 0 || source.Length == 0)
            return BadRequest("Key and source text are required");

        if (await database.TranslationUnits.AnyAsync(u => u.ProjectId == projectId && u.Key == key))
        {
            return Conflict("Translation key already exists");
        }

        var unit = new TranslationUnit
        {
            ProjectId = projectId, Key = key, SourceText = source,
            Description = request.Description, SourceRevision = 1,
            SourceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
        };
        await database.TranslationUnits.AddAsync(unit);

        var languages = await database.TranslationProjectLanguages.AsNoTracking()
            .Where(l => l.ProjectId == projectId && l.Enabled).ToListAsync();
        foreach (var language in languages)
        {
            var isSource = language.LanguageCode == project.SourceLanguage;
            await database.TranslationValues.AddAsync(new TranslationValue
            {
                ProjectId = projectId, Key = key, LanguageCode = language.LanguageCode,
                Text = isSource ? source : string.Empty,
                LastImportedText = isSource ? source : string.Empty,
                State = isSource ? TranslationState.Translated : TranslationState.NeedsTranslation,
                SourceRevisionTranslatedFrom = unit.SourceRevision,
                SourceTextTranslatedFrom = source,
                LastEditedByUserId = isSource ? actor.Id : null,
                LastEditedAt = isSource ? DateTime.UtcNow : null,
            });
        }

        var sourceRevision = ++project.CurrentRevision;
        await database.TranslationChanges.AddAsync(new TranslationChange
        {
            ProjectId = projectId, Key = key, LanguageCode = project.SourceLanguage,
            ActorUserId = actor.Id, ContributorUserId = actor.Id,
            Kind = TranslationChangeKind.UserEdit,
            OldText = string.Empty, NewText = source,
            OldState = TranslationState.NeedsTranslation, NewState = TranslationState.Translated,
            SourceRevision = unit.SourceRevision, ProjectRevision = sourceRevision,
            CountsAsContribution = true,
        });

        var subscriptions = await database.TranslationSubscriptions.AsNoTracking()
            .Where(s => s.ProjectId == projectId && s.EventType == TranslationSubscriptionEventType.NewKey &&
                s.UserId != actor.Id)
            .ToListAsync();
        foreach (var subscription in subscriptions)
        {
            await database.UserNotifications.AddAsync(new UserNotification
            {
                UserId = subscription.UserId,
                DeduplicationKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"new-key:{projectId}:{key}:{subscription.LanguageCode}:{sourceRevision}"))),
                Type = UserNotificationType.TranslationKeyAdded,
                Message = $"New {subscription.LanguageCode} translation key: {key}",
                TargetPath = $"/translations/{project.Slug}/{subscription.LanguageCode}/edit" +
                    $"?key={Uri.EscapeDataString(key)}",
            });
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Translation key {Key} added to project {ProjectId} by {Email}", key, projectId,
            actor.Email);
        return NoContent();
    }
}
