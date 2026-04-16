namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Emails;

/// <summary>
///   Analyzes accumulated bounces for an address and decides whether to disable
///   emails temporarily. Schedules a resume job with an increasing backoff when disabled.
/// </summary>
[DisableConcurrentExecution(500)]
public class BounceHandlingJob
{
    private readonly ApplicationDbContext database;
    private readonly ILogger<BounceHandlingJob> logger;

    public BounceHandlingJob(ApplicationDbContext database, ILogger<BounceHandlingJob> logger)
    {
        this.database = database;
        this.logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task HandleAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var bounce = await database.EmailBounces
            .FirstOrDefaultAsync(b => b.Email == normalizedEmail, cancellationToken);

        bounce ??= await database.EmailBounces
            .FirstOrDefaultAsync(b => b.NormalizedEmail == normalizedEmail, cancellationToken);

        if (bounce == null)
        {
            logger.LogWarning("Bounce handling: no record for {Email}", normalizedEmail);
            return;
        }

        // Threshold: 3 or more bounces within a month
        var withinMonth = bounce.FirstBounceUtc >= now.AddDays(-30);
        if (!withinMonth || bounce.OutstandingBounces < 3)
        {
            bounce.OutstandingBounces = 0;
            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Bounce handling: {Email} had bounces but below monthly threshold; count reset.",
                normalizedEmail);
            return;
        }

        // Reaching threshold: disable emails for a while
        logger.LogInformation("Bounce handling: disabling emails for {Email} due to frequent bounces.",
            normalizedEmail);

        // Prefer user accounts; presence of a user should override direct preferences
        var user = await database.Users
            .Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail || u.Email == normalizedEmail,
                cancellationToken);

        if (user != null)
        {
            if (user.EmailPreferences == null)
            {
                user.EmailPreferences = new UserEmailPreferences
                {
                    User = user,
                };
                await database.UserEmailPreferences.AddAsync(user.EmailPreferences, cancellationToken);
            }

            if (user.EmailPreferences.DisableAllEmails)
            {
                logger.LogInformation("Bounce handling: {Email} already disabled emails.", normalizedEmail);
                return;
            }

            user.EmailPreferences.DisableAllEmails = true;
        }
        else
        {
            var direct = await database.DirectEmailPreferences
                .FirstOrDefaultAsync(d => d.NormalizedEmail == normalizedEmail, cancellationToken);

            if (direct == null)
            {
                // Create new direct prefs to be able to block
                direct = new DirectEmailPreferences
                {
                    Email = bounce.Email,
                    NormalizedEmail = normalizedEmail,
                };
                await database.DirectEmailPreferences.AddAsync(direct, cancellationToken);
            }

            if (direct.DisableAllEmails)
            {
                logger.LogInformation("Bounce handling: {Email} already disabled emails.", normalizedEmail);
                return;
            }

            direct.DisableAllEmails = true;
        }

        // Mark that the system disabled emails so we can safely re-enable later
        bounce.DisabledBySystem = true;

        // Increase backoff by a couple of weeks each time, capped at 52 weeks
        var nextBackoff = bounce.BackoffWeeks <= 0 ? 1 : Math.Min(52, bounce.BackoffWeeks + 2);
        bounce.BackoffWeeks = nextBackoff;

        await database.LogEntries.AddAsync(new LogEntry($"Disabled emails for email address {normalizedEmail}")
        {
            TargetUserId = user?.Id,
        }, cancellationToken);

        await database.SaveChangesAsync(cancellationToken);

        BackgroundJob.Schedule<ResumeBouncedEmailJob>(j => j.ResumeAsync(normalizedEmail, CancellationToken.None),
            TimeSpan.FromDays(7 * nextBackoff));
    }
}
