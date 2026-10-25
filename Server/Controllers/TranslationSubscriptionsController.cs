namespace RevolutionaryWebApp.Server.Controllers;

using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;
using Shared.Models;
using Shared.Models.Enums;

// TODO: would be nice if restricted users could unsubscribe (but not make new subscriptions)
[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
public class TranslationSubscriptionsController : Controller
{
    private readonly NotificationsEnabledDb database;

    public TranslationSubscriptionsController(NotificationsEnabledDb database)
    {
        this.database = database;
    }

    [HttpGet]
    public async Task<ActionResult<TranslationSubscriptionDTO>> Get(long projectId, string language)
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var types = await database.TranslationSubscriptions.AsNoTracking().Where(s => s.UserId == userId &&
                s.ProjectId == projectId && s.LanguageCode == language).Select(s => s.EventType)
            .ToListAsync();
        return new TranslationSubscriptionDTO
        {
            ProjectId = projectId,
            LanguageCode = language,
            NewKeys = types.Contains(TranslationSubscriptionEventType.NewKey),
            TranslationUpdates = types.Contains(TranslationSubscriptionEventType.TranslationUpdate),
        };
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] TranslationSubscriptionDTO request)
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        if (!await database.TranslationProjectLanguages.AnyAsync(l => l.ProjectId == request.ProjectId &&
                l.LanguageCode == request.LanguageCode && l.Enabled))
        {
            return NotFound();
        }

        var hasSubscription = request.NewKeys || request.TranslationUpdates;
        if (hasSubscription)
        {
            // Upon subscribing, mark the user as having seen the prompt so we avoid extra DB count queries
            var user = await database.Users.FindAsync(userId);
            if (user == null)
                return Unauthorized();

            await user.ComputeUserGroups(database);
            user.TranslationSubscriptionPromptShown = true;
        }

        var existing = await database.TranslationSubscriptions.Where(s => s.UserId == userId &&
                s.ProjectId == request.ProjectId && s.LanguageCode == request.LanguageCode)
            .ToListAsync();
        SetSubscription(TranslationSubscriptionEventType.NewKey, request.NewKeys);
        SetSubscription(TranslationSubscriptionEventType.TranslationUpdate, request.TranslationUpdates);
        await database.SaveChangesAsync();
        return NoContent();

        void SetSubscription(TranslationSubscriptionEventType eventType, bool enabled)
        {
            var current = existing.FirstOrDefault(s => s.EventType == eventType);
            if (enabled && current == null)
            {
                database.TranslationSubscriptions.Add(new TranslationSubscription
                {
                    UserId = userId, ProjectId = request.ProjectId,
                    LanguageCode = request.LanguageCode, EventType = eventType,
                });
            }
            else if (!enabled && current != null)
            {
                database.TranslationSubscriptions.Remove(current);
            }
        }
    }

    [HttpGet("prompt")]
    public async Task<ActionResult<TranslationSubscriptionPromptDTO>> Prompt()
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var shown = await database.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.TranslationSubscriptionPromptShown).FirstOrDefaultAsync();
        if (shown)
            return new TranslationSubscriptionPromptDTO { ShowPrompt = false };

        var count = await database.TranslationChanges.CountAsync(c => c.ContributorUserId == userId &&
            c.CountsAsContribution);
        return new TranslationSubscriptionPromptDTO { ShowPrompt = count >= 3 };
    }

    [HttpPost("prompt/dismiss")]
    public async Task<IActionResult> Dismiss()
    {
        var user = await database.Users.FindAsync(HttpContext.AuthenticatedUserOrThrow().Id);
        if (user == null)
            return Unauthorized();

        await user.ComputeUserGroups(database);
        user.TranslationSubscriptionPromptShown = true;
        await database.SaveChangesAsync();
        return NoContent();
    }
}
