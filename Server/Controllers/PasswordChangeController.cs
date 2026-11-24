namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Npgsql;
using Services;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/password/change")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PasswordChangeController : Controller
{
    private const string InitialPasswordInstructions =
        "Sign out and use the Forgot password page to set your first password by email.";

    private const string AccountChanged = "Your account settings changed. Reopen the form and try again.";

    private readonly NotificationsEnabledDb database;
    private readonly TwoFactorService twoFactor;
    private readonly ILogger<PasswordChangeController> logger;

    public PasswordChangeController(NotificationsEnabledDb database, TwoFactorService twoFactor,
        ILogger<PasswordChangeController> logger)
    {
        this.database = database;
        this.twoFactor = twoFactor;
        this.logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<PasswordChangeStatusDTO>> GetStatus()
    {
        if (HttpContext.AuthenticatedUserSession() == null)
            return Unauthorized();

        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var status = await database.Users.Where(user => user.Id == userId)
            .Select(user => new PasswordChangeStatusDTO
            {
                HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
                TwoFactorEnabled = user.TwoFactorAuthentication != null && user.TwoFactorAuthentication.Enabled,
            })
            .SingleOrDefaultAsync(HttpContext.RequestAborted);

        return status == null ? NotFound() : status;
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitCategories.LoginLimit)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request)
    {
        if (HttpContext.AuthenticatedUserSession() == null)
            return Unauthorized();

        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var cancellationToken = HttpContext.RequestAborted;

        try
        {
            // Read both credentials in the same transaction as the update. Concurrent password changes
            // must not both succeed using the old password.
            await using var transaction = database.Database.IsRelational() ?
                await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) :
                null;

            var user = await database.Users.Include(user => user.TwoFactorAuthentication)
                .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
            if (user == null)
                return NotFound();

            if (string.IsNullOrEmpty(user.PasswordHash))
                return BadRequest(InitialPasswordInstructions);

            if (!Passwords.CheckPassword(user.PasswordHash, request.CurrentPassword))
                return BadRequest("The current password is incorrect.");

            if (user.TwoFactorAuthentication is { Enabled: true } credential)
            {
                if (string.IsNullOrWhiteSpace(request.TwoFactorCode))
                    return BadRequest("Two-factor authentication is enabled. Reopen the form and enter a code.");

                if (!await twoFactor.VerifyCode(credential, request.TwoFactorCode))
                {
                    // Preserve failed attempts and lockouts even though the password change was rejected.
                    if (transaction != null)
                        await transaction.CommitAsync(cancellationToken);

                    return BadRequest("Invalid or already used code, or too many attempts. Try again later.");
                }
            }

            user.PasswordHash = Passwords.CreateSaltedPasswordHash(request.NewPassword);
            await database.ActionLogEntries.AddAsync(new ActionLogEntry("Password changed from profile")
            {
                PerformedById = userId,
            }, cancellationToken);
            await database.UserNotifications.AddAsync(new UserNotification
            {
                UserId = userId,
                DeduplicationKey = $"password-changed:{Guid.NewGuid()}",
                Type = UserNotificationType.PasswordChanged,
                Priority = NotificationPriority.High,
                Message = "Your password was changed. If this wasn't you, secure your account immediately.",
                TargetPath = "/me",
            }, cancellationToken);

            // Password hashes do not affect any profile DTOs.
            var notificationSender = database.AutoSendNotifications;
            database.AutoSendNotifications = null;
            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                database.AutoSendNotifications = notificationSender;
            }

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Password changed from profile for user {UserId}", userId);
            return Ok();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
                                                  {
                                                      SqlState: PostgresErrorCodes.SerializationFailure,
                                                  })
        {
            return Conflict(AccountChanged);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            return Conflict(AccountChanged);
        }
    }
}
