namespace RevolutionaryWebApp.Server.Controllers;

using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Utilities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Emails;
using RevolutionaryWebApp.Shared.Models;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class EmailPreferencesController : Controller
{
    private readonly ILogger<EmailPreferencesController> logger;
    private readonly ApplicationDbContext database;
    private readonly ITimeLimitedDataProtector tokenProtector;

    public EmailPreferencesController(ILogger<EmailPreferencesController> logger, ApplicationDbContext database,
        IDataProtectionProvider dataProtectionProvider)
    {
        this.database = database;
        this.logger = logger;
        tokenProtector = dataProtectionProvider.CreateProtector(EmailPreferenceToken.ProtectionPurpose)
            .ToTimeLimitedDataProtector();
    }

    [HttpGet]
    public async Task<ActionResult<EmailPreferencesDTO>> GetForCurrentUser()
    {
        var user = HttpContext.AuthenticatedUser();

        if (user == null)
            return Unauthorized();

        // Ensure preferences are loaded from DB
        var dbUser = await database.Users.AsNoTracking().Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.Id == user.Id, HttpContext.RequestAborted);

        if (dbUser == null)
            return Unauthorized();

        var prefs = dbUser.EmailPreferences ?? new UserEmailPreferences { UserId = dbUser.Id };
        return Ok(prefs.GetDTO());
    }

    [HttpGet("byToken")]
    [AllowAnonymous]
    public async Task<ActionResult<EmailPreferencesDTO>> GetByToken([FromQuery] [Required] string token)
    {
        var decoded = EmailPreferenceToken.TryToLoadFromString(tokenProtector, token);
        if (decoded == null)
            return BadRequest("Invalid or expired token");

        var email = decoded.Email;
        var normalized = Normalization.NormalizeEmail(email);

        // If there is a user with this email, prefer user preferences
        var user = await database.Users.Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized || u.Email == email,
                HttpContext.RequestAborted);

        if (user != null)
        {
            if (user.EmailPreferences != null)
                return Ok(user.EmailPreferences.GetDTO());

            return Ok(new UserEmailPreferences { UserId = user.Id }.GetDTO());
        }

        // Else use DirectEmailPreferences if exists
        // TODO: should we actually not match things by normalized email here?
        // Just in case there is some false sharing?
        var direct = await database.DirectEmailPreferences
            .FirstOrDefaultAsync(d => d.NormalizedEmail == normalized || d.Email == email,
                HttpContext.RequestAborted);

        if (direct != null)
            return Ok(direct.GetDTO());

        // Not existing yet -> return defaults
        var defaults = new DirectEmailPreferences { Email = email, NormalizedEmail = normalized };
        return Ok(defaults.GetDTO());
    }

    [HttpPut]
    public async Task<ActionResult<EmailPreferencesDTO>> UpdateForCurrentUser([FromBody] EmailPreferencesDTO update)
    {
        var currentUser = HttpContext.AuthenticatedUser();
        if (currentUser == null)
            return Unauthorized();

        var dbUser = await database.Users.Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.Id == currentUser.Id, HttpContext.RequestAborted);

        if (dbUser == null)
            return Unauthorized();

        if (dbUser.EmailPreferences == null)
        {
            dbUser.EmailPreferences = new UserEmailPreferences { UserId = dbUser.Id };
            await database.UserEmailPreferences.AddAsync(dbUser.EmailPreferences, HttpContext.RequestAborted);
        }

        var (changes, _, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(dbUser.EmailPreferences, update);

        if (!changes)
        {
            logger.LogInformation("No changes to user email preferences");
            return Ok(dbUser.EmailPreferences.GetDTO());
        }

        await database.SaveChangesAsync(HttpContext.RequestAborted);

        logger.LogInformation("User {Email} updated their email preferences", dbUser.Email);

        return Ok(dbUser.EmailPreferences.GetDTO());
    }

    [HttpPut("byToken")]
    [AllowAnonymous]
    public async Task<ActionResult<EmailPreferencesDTO>> UpdateByToken([FromQuery] [Required] string token,
        [FromBody] EmailPreferencesDTO update)
    {
        var decoded = EmailPreferenceToken.TryToLoadFromString(tokenProtector, token);
        if (decoded == null)
            return BadRequest("Invalid or expired token");

        var email = decoded.Email;
        var normalized = Normalization.NormalizeEmail(email);

        // If a user exists with this email, user must log in to update details
        var user = await database.Users.Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalized || u.Email == email,
                HttpContext.RequestAborted);

        if (user != null)
        {
            return this.WorkingForbid(
                "You must log in to update your email preferences (this email is associated with an account)");
        }

        // Otherwise, update direct preferences if no account
        // TODO: should we actually not match things by normalized email here?
        // Just in case there is some false sharing?
        var direct = await database.DirectEmailPreferences
            .FirstOrDefaultAsync(d => d.NormalizedEmail == normalized || d.Email == email,
                HttpContext.RequestAborted);

        if (direct == null)
        {
            direct = new DirectEmailPreferences { Email = email, NormalizedEmail = normalized };
            await database.DirectEmailPreferences.AddAsync(direct, HttpContext.RequestAborted);
        }

        var (changes, _, _) = ModelUpdateApplyHelper.ApplyUpdateRequestToModel(direct, update);

        if (!changes)
        {
            logger.LogInformation("No changes to direct email preferences");
            return Ok(direct.GetDTO());
        }

        logger.LogInformation("Email with no account updated their direct email preferences: {Email}", email);

        await database.SaveChangesAsync(HttpContext.RequestAborted);
        return Ok(direct.GetDTO());
    }
}
