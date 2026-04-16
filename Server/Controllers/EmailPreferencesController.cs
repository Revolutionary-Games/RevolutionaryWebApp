namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Emails;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class EmailPreferencesController : Controller
{
    private readonly ILogger<EmailPreferencesController> logger;
    private readonly ApplicationDbContext database;
    private readonly IDataProtector tokenProtector;

    public EmailPreferencesController(ILogger<EmailPreferencesController> logger, ApplicationDbContext database,
        IDataProtectionProvider dataProtectionProvider)
    {
        this.database = database;
        this.logger = logger;
        tokenProtector = dataProtectionProvider.CreateProtector(EmailPreferenceToken.ProtectionPurpose);
    }

    // DTO used for reading and updating preferences
    public record EmailPreferencesDto(bool DisableAllEmails,
        bool AllowSiteAnnouncement,
        bool AllowPasswordReset,
        bool AllowConfirmEmail,
        bool AllowNotifications,
        bool AllowPushBuildStatus,
        bool AllowCommitBuildStatus);

    private static EmailPreferencesDto ToDto(EmailPreferences model) => new(model.DisableAllEmails,
        model.AllowSiteAnnouncement,
        model.AllowPasswordReset,
        model.AllowConfirmEmail,
        model.AllowNotifications,
        model.AllowPushBuildStatus,
        model.AllowCommitBuildStatus);

    private static void Apply(EmailPreferences model, EmailPreferencesDto dto)
    {
        model.DisableAllEmails = dto.DisableAllEmails;
        model.AllowSiteAnnouncement = dto.AllowSiteAnnouncement;
        model.AllowPasswordReset = dto.AllowPasswordReset;
        model.AllowConfirmEmail = dto.AllowConfirmEmail;
        model.AllowNotifications = dto.AllowNotifications;
        model.AllowPushBuildStatus = dto.AllowPushBuildStatus;
        model.AllowCommitBuildStatus = dto.AllowCommitBuildStatus;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<EmailPreferencesDto>> GetForCurrentUser(CancellationToken cancellationToken)
    {
        var userIdStr = User.Claims
            .FirstOrDefault(c => c.Type.EndsWith("nameidentifier", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !long.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await database.Users.Include(u => u.EmailPreferences).FirstOrDefaultAsync(u => u.Id == userId,
            cancellationToken);

        if (user == null)
            return Unauthorized();

        var prefs = user.EmailPreferences ?? new UserEmailPreferences { UserId = user.Id };
        return Ok(ToDto(prefs));
    }

    [HttpGet("by-token")]
    [AllowAnonymous]
    public async Task<ActionResult<EmailPreferencesDto>> GetByToken([FromQuery] [Required] string token,
        CancellationToken cancellationToken)
    {
        var decoded = EmailPreferenceToken.TryToLoadFromString(tokenProtector, token);
        if (decoded == null)
            return BadRequest("Invalid token");

        var email = decoded.Email;
        var normalized = email.ToUpperInvariant();

        // If there is a user with this email, prefer user preferences
        var user = await database.Users.Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized || u.Email == email, cancellationToken);

        if (user?.EmailPreferences != null)
            return Ok(ToDto(user.EmailPreferences));

        // Else use DirectEmailPreferences if exists
        var direct = await database.DirectEmailPreferences
            .FirstOrDefaultAsync(d => d.NormalizedEmail == normalized || d.Email == email, cancellationToken);

        if (direct != null)
            return Ok(ToDto(direct));

        // Not existing yet -> return defaults
        var defaults = new DirectEmailPreferences { Email = email, NormalizedEmail = normalized };
        return Ok(ToDto(defaults));
    }

    [HttpPut]
    [Authorize]
    public async Task<ActionResult<EmailPreferencesDto>> UpdateForCurrentUser([FromBody] EmailPreferencesDto update,
        CancellationToken cancellationToken)
    {
        var userIdStr = User.Claims
            .FirstOrDefault(c => c.Type.EndsWith("nameidentifier", StringComparison.OrdinalIgnoreCase))?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !long.TryParse(userIdStr, out var userId))
            return Unauthorized();

        var user = await database.Users.Include(u => u.EmailPreferences).FirstOrDefaultAsync(u => u.Id == userId,
            cancellationToken);
        if (user == null)
            return Unauthorized();

        if (user.EmailPreferences == null)
        {
            user.EmailPreferences = new UserEmailPreferences { UserId = user.Id };
            database.UserEmailPreferences.Add(user.EmailPreferences);
        }

        Apply(user.EmailPreferences, update);
        await database.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(user.EmailPreferences));
    }

    [HttpPut("by-token")]
    [AllowAnonymous]
    public async Task<ActionResult<EmailPreferencesDto>> UpdateByToken([FromQuery] [Required] string token,
        [FromBody] EmailPreferencesDto update, CancellationToken cancellationToken)
    {
        var decoded = EmailPreferenceToken.TryToLoadFromString(tokenProtector, token);
        if (decoded == null)
            return BadRequest("Invalid token");

        var email = decoded.Email;
        var normalized = email.ToUpperInvariant();

        // If a user exists with this email, update user's preferences
        var user = await database.Users.Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized || u.Email == email, cancellationToken);

        if (user != null)
        {
            if (user.EmailPreferences == null)
            {
                user.EmailPreferences = new UserEmailPreferences { UserId = user.Id };
                database.UserEmailPreferences.Add(user.EmailPreferences);
            }

            Apply(user.EmailPreferences, update);
            await database.SaveChangesAsync(cancellationToken);
            return Ok(ToDto(user.EmailPreferences));
        }

        // Otherwise, upsert direct preferences
        var direct = await database.DirectEmailPreferences
            .FirstOrDefaultAsync(d => d.NormalizedEmail == normalized || d.Email == email, cancellationToken);

        if (direct == null)
        {
            direct = new DirectEmailPreferences { Email = email, NormalizedEmail = normalized };
            database.DirectEmailPreferences.Add(direct);
        }

        Apply(direct, update);
        await database.SaveChangesAsync(cancellationToken);
        return Ok(ToDto(direct));
    }
}
