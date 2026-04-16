namespace RevolutionaryWebApp.Server.Jobs;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   After a new user account is created, this job runs once (with a delay)
///   to remove any existing direct email preferences for the same email address.
///   The user-specific preferences should take precedence over direct email preferences.
/// </summary>
/// <remarks>
///   <para>
///     TODO: once email verification is implemented, only perform the deletion if the
///     user's email has been verified by the time this job runs.
///   </para>
/// </remarks>
public class CleanupDirectEmailPreferencesForUserJob
{
    private readonly ILogger<CleanupDirectEmailPreferencesForUserJob> logger;
    private readonly NotificationsEnabledDb database;

    public CleanupDirectEmailPreferencesForUserJob(ILogger<CleanupDirectEmailPreferencesForUserJob> logger,
        NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(string email, CancellationToken cancellationToken)
    {
        // Re-check that the user still exists before attempting any clean-up
        var user = await database.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user == null)
        {
            logger.LogInformation("Cleanup skipped: user with email {Email} no longer exists.", email);
            return;
        }

        var normalized = Normalization.NormalizeEmail(user.Email);

        var directPrefs = await database.DirectEmailPreferences
            .Where(p => p.NormalizedEmail == normalized || p.Email == user.Email)
            .ToListAsync(cancellationToken);

        if (directPrefs.Count == 0)
        {
            logger.LogDebug("No direct email preferences found to clean up for normalized email {Email}.", normalized);
            return;
        }

        logger.LogInformation(
            "Removing {Count} direct email preference record(s) for email {Email} (normalized: {Normalized}).",
            directPrefs.Count, user.Email, normalized);

        database.DirectEmailPreferences.RemoveRange(directPrefs);

        await database.LogEntries.AddAsync(new LogEntry("Removed direct email preferences for user")
        {
            TargetUserId = user.Id,
        }, cancellationToken);

        await database.SaveChangesAsync(cancellationToken);
    }
}
