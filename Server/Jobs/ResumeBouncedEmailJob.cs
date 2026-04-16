namespace RevolutionaryWebApp.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Resumes emails for an address after a cooldown, but only if the system was the one
///   that disabled them. If the user has disabled emails manually, this job does nothing.
/// </summary>
[DisableConcurrentExecution(300)]
public class ResumeBouncedEmailJob
{
    private readonly ApplicationDbContext database;
    private readonly ILogger<ResumeBouncedEmailJob> logger;

    public ResumeBouncedEmailJob(ApplicationDbContext database, ILogger<ResumeBouncedEmailJob> logger)
    {
        this.database = database;
        this.logger = logger;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task ResumeAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        var bounce = await database.EmailBounces
            .FirstOrDefaultAsync(b => b.Email == normalizedEmail, cancellationToken);

        bounce ??= await database.EmailBounces
            .FirstOrDefaultAsync(b => b.NormalizedEmail == normalizedEmail, cancellationToken);

        if (bounce == null)
        {
            logger.LogInformation("Resume emails: no bounce record for {Email}", normalizedEmail);
            return;
        }

        if (!bounce.DisabledBySystem)
        {
            logger.LogInformation("Resume emails: {Email} not disabled by system; skipping.", normalizedEmail);
            return;
        }

        // Try user preferences first
        var user = await database.Users
            .Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);

        bool changed = false;

        if (user != null)
        {
            if (user.EmailPreferences != null && user.EmailPreferences.DisableAllEmails)
            {
                user.EmailPreferences.DisableAllEmails = false;
                changed = true;
            }
        }
        else
        {
            var direct = await database.DirectEmailPreferences
                .FirstOrDefaultAsync(d => d.NormalizedEmail == normalizedEmail, cancellationToken);

            if (direct != null && direct.DisableAllEmails)
            {
                direct.DisableAllEmails = false;
                changed = true;
            }
        }

        if (changed)
        {
            bounce.DisabledBySystem = false;
            bounce.OutstandingBounces = 0;
            await database.LogEntries.AddAsync(new LogEntry($"Resumed emails for email address {normalizedEmail}"),
                cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Resume emails: re-enabled emails for {Email}", normalizedEmail);
        }
        else
        {
            logger.LogInformation("Resume emails: preferences for {Email} not blocked or user-managed; no change.",
                normalizedEmail);
        }
    }
}
