namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Utilities;
using Filters;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared;
using Shared.Forms;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/password")]
public class PasswordResetController : Controller
{
    private readonly ApplicationDbContext database;
    private readonly ILogger<PasswordResetController> logger;
    private readonly IMailQueue mailQueue;
    private readonly IConfiguration configuration;
    private readonly ITimeLimitedDataProtector protector;

    public PasswordResetController(ILogger<PasswordResetController> logger, ApplicationDbContext database,
        IMailQueue mailQueue, IConfiguration configuration, IDataProtectionProvider dataProtectionProvider)
    {
        this.database = database;
        this.logger = logger;
        this.mailQueue = mailQueue;
        this.configuration = configuration;
        protector = dataProtectionProvider.CreateProtector(PasswordResetToken.ProtectionPurpose)
            .ToTimeLimitedDataProtector();
    }

    /// <summary>
    ///   Request a password reset email. Always returns generic success regardless of whether an account exists.
    /// </summary>
    [HttpPost("forgot")]
    [EnableRateLimiting(RateLimitCategories.PasswordReset)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        // Disallow if user is already authenticated
        var user = HttpContext.AuthenticatedUser();
        if (user != null)
        {
            return Forbid();
        }

        request.Email = request.Email.Trim();

        // Normalize email and look up user
        var normalized = Normalization.NormalizeEmail(request.Email);

        var targetUser = await database.Users.AsNoTracking()
            .Where(u => u.Email == request.Email)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        targetUser ??= await database.Users.AsNoTracking()
            .Where(u => u.NormalizedEmail == normalized)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (targetUser != null)
        {
            try
            {
                await SendPasswordResetEmailAsync(targetUser);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to queue password reset email for {Email}", targetUser.Email);
            }
        }

        // Always respond with success message to avoid account enumeration
        return Ok("If there is an account associated with that email, password reset instructions have been sent.");
    }

    /// <summary>
    ///   Completes a password reset using a time-limited token and a new password.
    ///   Always disallowed for authenticated users.
    /// </summary>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        // Disallow if user is already authenticated
        var user = HttpContext.AuthenticatedUser();
        if (user != null)
        {
            return Forbid();
        }

        var decoded = PasswordResetToken.TryToLoadFromString(protector, request.Token);
        if (decoded == null)
        {
            return BadRequest("Invalid or expired token.");
        }

        var targetUser = await database.Users
            .Where(u => u.Email == decoded.Email)
            .FirstOrDefaultAsync(HttpContext.RequestAborted);

        if (targetUser == null)
        {
            // Generic message to avoid disclosing account existence details
            return BadRequest("Invalid or expired token.");
        }

        // Validate password length explicitly (even though DTO validates) to be safe
        if (string.IsNullOrWhiteSpace(request.Password) ||
            request.Password.Length < AppInfo.MinPasswordLength ||
            request.Password.Length > AppInfo.MaxPasswordLength)
        {
            return BadRequest("Invalid password length.");
        }

        targetUser.PasswordHash = Passwords.CreateSaltedPasswordHash(request.Password);

        await database.ActionLogEntries.AddAsync(new ActionLogEntry("Password reset by email")
        {
            PerformedById = targetUser.Id,
        });

        await database.SaveChangesAsync(HttpContext.RequestAborted);

        logger.LogInformation("Password reset completed for {Email}", targetUser.Email);

        // Send a notification email to the user to let them know their password has been reset, which is a very
        // important email
        await SendNoticeEmailToUserAsync(targetUser);

        return Ok("Password has been reset.");
    }

    private async Task SendPasswordResetEmailAsync(User targetUser)
    {
        var token = new PasswordResetToken(targetUser.Email).ToEncodedString(protector);
        var baseUrl = configuration.GetBaseUrl();

        var resetUrl = new Uri(baseUrl, $"/reset-password/{Uri.EscapeDataString(token)}").ToString();

        var subject = "Password reset instructions";
        var baseHtml =
            $"<p>We received a request to reset your password for the account associated with " +
            $"<b>{System.Net.WebUtility.HtmlEncode(targetUser.Email)}</b>.</p>" +
            $"<p>If you made this request, click the link below to reset your password. This link expires in 1 hour.</p>" +
            $"<p><a href=\"{resetUrl}\">Reset your password</a></p>";

        var baseText =
            $"We received a request to reset your password for the account associated with {targetUser.Email}.\n" +
            "If you made this request, use the link below within 1 hour to reset your password.\n" +
            resetUrl + "\n\n";

        var (htmlWithFooter, textWithFooter) = await EmailHelpers.GenerateFooterAsync(database,
            protector, configuration, targetUser.Email, EmailReason.PasswordReset, targetUser.Id,
            baseHtml, baseText,
            "You received this email because a password reset was requested for your account.",
            HttpContext.RequestAborted);

        var request = new MailRequest(targetUser.Email, subject, EmailReason.PasswordReset)
        {
            HtmlBody = htmlWithFooter,
            PlainTextBody = textWithFooter,
            RecipientUserId = targetUser.Id,
        };

        await mailQueue.SendEmail(request, HttpContext.RequestAborted);
    }

    private async Task SendNoticeEmailToUserAsync(User targetUser)
    {
        var baseUrl = configuration.GetBaseUrl();

        var subject = "Your password has been reset";
        var baseHtml =
            $"<p>The password for your account <b>{System.Net.WebUtility.HtmlEncode(targetUser.UserName)}</b> for " +
            $"Thrive has been reset.</p>" +
            $"<p>This reset was performed with a password reset request email. If you did not request this reset, " +
            $"your account(s) have been compromised!</p>" +
            $"<p><a href=\"{baseUrl}\">Visit the website</a></p>";

        var baseText =
            $"The password for your account {targetUser.UserName} for Thrive has been reset.\n\n" +
            $"This reset was performed with a password reset request email. If you did not request this reset, " +
            $"your account(s) have been compromised!\n\n" +
            $"Visit the website: {baseUrl}\n\n";

        var (htmlWithFooter, textWithFooter) = await EmailHelpers.GenerateFooterAsync(database,
            protector, configuration, targetUser.Email, EmailReason.ImportantEmails, targetUser.Id,
            baseHtml, baseText,
            "You received this email because your account's password has been reset.",
            CancellationToken.None);

        var request = new MailRequest(targetUser.Email, subject, EmailReason.ImportantEmails)
        {
            HtmlBody = htmlWithFooter,
            PlainTextBody = textWithFooter,
            RecipientUserId = targetUser.Id,
        };

        await mailQueue.SendEmail(request, CancellationToken.None);
    }
}
