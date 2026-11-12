namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Shared.Models.Enums;
using Utilities;

/// <summary>
///   Sends three daily warnings and clears two-factor credentials no earlier than 72 hours after a request.
/// </summary>
[DisableConcurrentExecution(300)]
public class TwoFactorRecoveryJob : IJob
{
    private readonly NotificationsEnabledDb database;
    private readonly TwoFactorService twoFactor;
    private readonly IMailSender mail;
    private readonly IConfiguration configuration;
    private readonly ILogger<TwoFactorRecoveryJob> logger;

    public TwoFactorRecoveryJob(NotificationsEnabledDb database, TwoFactorService twoFactor, IMailSender mail,
        IConfiguration configuration, ILogger<TwoFactorRecoveryJob> logger)
    {
        this.database = database;
        this.twoFactor = twoFactor;
        this.mail = mail;
        this.configuration = configuration;
        this.logger = logger;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var pending = await database.TwoFactorAuthentications.AsNoTracking()
            .Where(t => t.Enabled && t.RecoveryRequestedAt != null).Select(t => t.UserId)
            .ToListAsync(cancellationToken);

        Exception? error = null;

        foreach (var userId in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var credential = await database.TwoFactorAuthentications.Include(t => t.User)
                    .FirstOrDefaultAsync(t => t.UserId == userId && t.Enabled && t.RecoveryRequestedAt != null,
                        cancellationToken);
                if (credential == null)
                    continue;

                var now = twoFactor.UtcNow;
                var requestedAt = credential.RecoveryRequestedAt!.Value;
                if (credential.LastRecoveryWarningAt > now.AddHours(-24))
                    continue;

                if (credential.RecoveryWarningsSent < 3)
                {
                    var availableAt = requestedAt + TwoFactorService.RecoveryDelay;
                    await mail.SendEmail(new MailRequest(credential.User.Email,
                        "Security warning: two-factor authentication removal requested", EmailReason.AccountSecurity)
                    {
                        RecipientUserId = credential.UserId,
                        PlainTextBody = $"Warning {credential.RecoveryWarningsSent + 1} of 3. " +
                            "Someone who passed your account's initial login step requested removal of " +
                            "two-factor authentication. It can be removed no earlier than " +
                            $"{availableAt:u}, after three warning emails. If this was not you, " +
                            "sign in and pass a two-factor challenge with your authenticator or a backup code " +
                            "to cancel removal, and secure your password and SSO accounts. " +
                            $"Sign in: {new Uri(configuration.GetBaseUrl(), "login")}",
                    }, cancellationToken);

                    ++credential.RecoveryWarningsSent;
                    credential.LastRecoveryWarningAt = now;
                    await twoFactor.SaveCredential(credential);
                }
                else if (now >= requestedAt + TwoFactorService.RecoveryDelay)
                {
                    // A full day must also have passed since the third successfully delivered warning.
                    twoFactor.ClearCredential(credential);
                    await database.LogEntries.AddAsync(
                        new LogEntry($"Completed delayed two-factor recovery for user {credential.UserId}"),
                        cancellationToken);
                    await twoFactor.RevokeOtherSessions(credential.UserId);
                    await twoFactor.SaveCredential(credential);
                    logger.LogInformation("Completed delayed two-factor recovery for user {UserId}", credential.UserId);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Never clear credentials if delivery fails or a concurrent challenge cancelled recovery.
                logger.LogWarning(e, "Failed to process two-factor recovery for user {UserId}", userId);
                database.ChangeTracker.Clear();

                error = e;
            }
        }

        if (error != null && !cancellationToken.IsCancellationRequested)
        {
            await database.LogEntries.AddAsync(
                new LogEntry($"Failed to process two-factor recovery for {pending.Count} users", error.ToString()),
                cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
