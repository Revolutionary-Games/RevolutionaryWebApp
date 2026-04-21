namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using DevCenterCommunication.Utilities;
using Filters;
using Hangfire;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
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

/// <summary>
///   Handles the new two-step local signup flow: start with email → confirm via a link → complete username.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class SignupsController : Controller
{
    private readonly ILogger<SignupsController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly ITokenVerifier csrfVerifier;
    private readonly IMailQueue mailQueue;
    private readonly IConfiguration configuration;
    private readonly ITimeLimitedDataProtector preferencesProtector;
    private readonly IBackgroundJobClient jobClient;

    public SignupsController(ILogger<SignupsController> logger, NotificationsEnabledDb database,
        ITokenVerifier csrfVerifier, IMailQueue mailQueue, IConfiguration configuration,
        IDataProtectionProvider dataProtectionProvider, IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.csrfVerifier = csrfVerifier;
        this.mailQueue = mailQueue;
        this.configuration = configuration;
        this.jobClient = jobClient;
        preferencesProtector = dataProtectionProvider.CreateProtector(EmailPreferenceToken.ProtectionPurpose)
            .ToTimeLimitedDataProtector();
    }

    [HttpPost("start")]
    [EnableRateLimiting(RateLimitCategories.RegistrationLimit)]
    public async Task<IActionResult> Start([FromBody] SignupStartRequest request)
    {
        // Globally allow disabling signups from configuration
        if (!configuration.GetValue("Registration:Enabled", false))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "Sorry, signups are currently disabled on this website.");
        }

        if (!csrfVerifier.IsValidCSRFToken(request.CSRF, null, false))
            return BadRequest("Invalid CSRF");

        if (!request.Email.Contains('@'))
            return BadRequest("Email is invalid");

        var cookieLookup = await Request.Cookies.GetUserFromSession(database, HttpContext.Connection.RemoteIpAddress);

        if (cookieLookup.User != null)
            return BadRequest("You are already logged in");

        // existing user check
        if (await database.Users.AsNoTracking().AnyAsync(u => u.Email == request.Email))
            return BadRequest("There is already an account associated with the given email");

        var normalized = Normalization.NormalizeEmail(request.Email);

        if (await database.Users.AsNoTracking().AnyAsync(u => u.NormalizedEmail == normalized))
            return BadRequest("There is already an account associated with the given email (when normalized)");

        var pending = await database.PendingUserSignups.FirstOrDefaultAsync(p => p.NormalizedEmail == normalized);

        var token = Guid.NewGuid().ToString("N");

        if (pending == null)
        {
            pending = new PendingUserSignup
            {
                Email = request.Email.Trim(),
                NormalizedEmail = normalized,
                Token = token,
                CreatedUtc = DateTime.UtcNow,
                LastEmailSentUtc = null,
                SendCount = 0,
            };

            await database.PendingUserSignups.AddAsync(pending);
        }
        else
        {
            // Rate limit excessive re-sends after a few attempts
            const int freeAttempts = 3;
            var now = DateTime.UtcNow;

            if (pending.SendCount >= freeAttempts && pending.LastEmailSentUtc.HasValue)
            {
                var sinceLast = now - pending.LastEmailSentUtc.Value;
                if (sinceLast < TimeSpan.FromHours(1))
                {
                    // Too many requests in a short time window
                    var retryIn = TimeSpan.FromHours(1) - sinceLast;
                    var message =
                        $"Too many confirmation emails requested. Please wait " +
                        $"{Math.Max(1, (int)Math.Ceiling(retryIn.TotalMinutes))} more minutes and try again. " +
                        "Also please check your spam folder if you don't see the email.";
                    return StatusCode(StatusCodes.Status429TooManyRequests, message);
                }
            }

            // refresh token to avoid link reuse
            pending.Token = token;
            pending.Email = request.Email.Trim();
            pending.NormalizedEmail = normalized;
        }

        // Send email
        var baseUrl = configuration.GetBaseUrl();
        var completePath = $"/complete-signup/{pending.Token}";
        var completeUrl = new Uri(baseUrl, completePath).ToString();

        var html =
            $"<p>Hello,</p><p>To complete creating your account, please confirm your email address by " +
            $"clicking the link below:</p><p><a href=\"{completeUrl}\">Complete your signup</a></p>" +
            $"<p>If you did not request this email, please ignore it and the account will not be created.</p>";
        var raw = $"Hello,\n\nTo complete creating your account, open this link: {completeUrl}\n\n" +
            "If you did not request this email, please ignore it and the account will not be created.";

        // add footer
        (html, raw) = await EmailHelpers.GenerateFooterAsync(database, preferencesProtector, configuration,
            request.Email, EmailReason.ConfirmEmail, null, html, raw,
            "You received this email because you requested to create an account.", HttpContext.RequestAborted);

        var mail = new MailRequest(request.Email, "Confirm your email to complete Thrive signup",
            EmailReason.ConfirmEmail)
        {
            HtmlBody = html,
            PlainTextBody = raw,
        };

        pending.SendCount += 1;
        pending.LastEmailSentUtc = DateTime.UtcNow;

        // Do the save and send in this order to only need to do a single database writing
        await database.SaveChangesAsync(HttpContext.RequestAborted);

        await mailQueue.SendEmail(mail, HttpContext.RequestAborted);

        logger.LogInformation("Started pending signup for {Email}", request.Email);
        return Ok();
    }

    [HttpGet("pending/{token}")]
    public async Task<ActionResult<PendingSignupInfoDTO>> GetPending([Required] string token)
    {
        var pending = await database.PendingUserSignups.AsNoTracking().FirstOrDefaultAsync(p => p.Token == token,
            HttpContext.RequestAborted);

        if (pending == null)
            return NotFound("Invalid or expired token");

        return new PendingSignupInfoDTO { Email = pending.Email };
    }

    [HttpPost("complete")]
    [EnableRateLimiting(RateLimitCategories.RegistrationLimit)]
    public async Task<IActionResult> Complete([FromForm] SignupCompleteRequest request)
    {
        // Block completing signups as well when disabled
        if (!configuration.GetValue("Registration:Enabled", false))
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                "Sorry, signups are currently disabled on this website.");
        }

        if (!csrfVerifier.IsValidCSRFToken(request.CSRF, null, false))
            return BadRequest("Invalid CSRF");

        var remoteAddress = HttpContext.Connection.RemoteIpAddress;

        var pending = await database.PendingUserSignups.FirstOrDefaultAsync(p => p.Token == request.Token,
            HttpContext.RequestAborted);

        if (pending == null)
            return BadRequest("Invalid or expired token (requesting a new email invalidates old links)");

        // Make sure an account doesn't already exist now
        if (await database.Users.AsNoTracking().AnyAsync(
                u => u.NormalizedEmail == pending.NormalizedEmail || u.Email == pending.Email,
                HttpContext.RequestAborted))
        {
            database.PendingUserSignups.Remove(pending);
            await database.SaveChangesAsync(HttpContext.RequestAborted);
            return BadRequest("There is already an account associated with this email");
        }

        // Validate password length explicitly (in addition to data annotations on DTO)
        if (string.IsNullOrEmpty(request.Password) ||
            request.Password.Length < AppInfo.MinPasswordLength ||
            request.Password.Length > AppInfo.MaxPasswordLength)
        {
            return BadRequest(
                $"Password must be between {AppInfo.MinPasswordLength} and {AppInfo.MaxPasswordLength} characters.");
        }

        var normalizedUserName = Normalization.NormalizeUserName(request.UserName);
        if (normalizedUserName != request.UserName)
        {
            return BadRequest("The given username was not in the correct format. It was normalized to: " +
                normalizedUserName);
        }

        if (await database.Users.AsNoTracking().AnyAsync(u => u.UserName == request.UserName,
                HttpContext.RequestAborted))
        {
            return BadRequest("Username is already taken");
        }

        var user = new User(pending.Email, request.UserName)
        {
            Local = true,
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName?.Trim(),
        };

        user.ComputeNormalizedEmail();

        // Set password hash for local login
        user.PasswordHash = Passwords.CreateSaltedPasswordHash(request.Password);

        await database.Users.AddAsync(user, HttpContext.RequestAborted);
        Models.User.OnNewUserCreated(user, jobClient);
        database.PendingUserSignups.Remove(pending);

        // We've created a user, so we no longer want to cancel
        await database.SaveChangesAsync(CancellationToken.None);

        // Auto-login: create or update current session and set cookie similar to LoginController
        // Create a new session for the user and set cookie using existing helpers
        var cookieLookup = await Request.Cookies.GetUserFromSession(database, HttpContext.Connection.RemoteIpAddress);
        var existingSession = cookieLookup.Session;

        var session = existingSession ?? new Session();
        if (existingSession == null)
            await database.Sessions.AddAsync(session, CancellationToken.None);

        session.LastLoggedIn = DateTime.UtcNow;
        session.User = user;
        session.UserId = user.Id;
        session.LastUsedFrom = HttpContext.Connection.RemoteIpAddress;
        session.LastUsed = DateTime.UtcNow;

        // Very important to load groups to the session cache, otherwise the login will be quite broken
        session.CachedUserGroups = await user.ComputeUserGroups(database);

        await database.SaveChangesAsync(CancellationToken.None);

        // Set cookie using shared helper
        LoginController.SetSessionCookie(session, Response,
            configuration.GetValue("UseSecureCookies", true));

        logger.LogInformation("Completed signup and logged in new user {User}", user.Email);

        logger.LogInformation("Successful login (user created) for user {Email} from {RemoteAddress}, session: {Id}",
            user.Email,
            remoteAddress, session.Id);

        // Client handles redirect
        return Ok();
    }
}
