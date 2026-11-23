namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Hangfire;
using Jobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using OtpNet;
using QRCoder;
using Shared.Models;
using Utilities;

/// <summary>
///   Owns authenticator setup, single-use challenges, and durable recovery state.
/// </summary>
public class TwoFactorService
{
    public static readonly TimeSpan ChallengeLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RecoveryDelay = TimeSpan.FromHours(72);

    private readonly NotificationsEnabledDb database;
    private readonly TimeProvider clock;
    private readonly IDataProtector protector;
    private readonly IMailQueue mail;
    private readonly IBackgroundJobClient jobs;
    private readonly string issuer;

    public TwoFactorService(NotificationsEnabledDb database, IDataProtectionProvider protection, TimeProvider clock,
        IMailQueue mail, IBackgroundJobClient jobs, IConfiguration configuration)
    {
        this.database = database;
        this.clock = clock;
        this.mail = mail;
        this.jobs = jobs;
        protector = protection.CreateProtector("RevolutionaryWebApp.TwoFactor.v1");
        issuer = $"Thrive Web ({configuration.GetBaseUrl().Host})";
    }

    // This needs to be done like this to make tests work
    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public static List<string> GetBackupCodeHashes(TwoFactorAuthentication credential)
    {
        return JsonSerializer.Deserialize<List<string>>(credential.BackupCodeHashes) ?? new List<string>();
    }

    public async Task<TwoFactorStatusDTO> GetStatus(long userId)
    {
        var credential = await database.TwoFactorAuthentications.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId);
        return new TwoFactorStatusDTO
        {
            Enabled = credential?.Enabled == true,
            BackupCodesRemaining = credential?.Enabled == true ? GetBackupCodeHashes(credential).Count : 0,
            RecoveryAvailableAt = credential?.RecoveryRequestedAt + RecoveryDelay,
        };
    }

    public async Task<TwoFactorSetupDTO> CreateSetup(User user, Session session)
    {
        if (session.LastLoggedIn == null || UtcNow - session.LastLoggedIn > TimeSpan.FromMinutes(30))
            throw Error("Please log in again, for security reasons, before setting up two-factor authentication.");

        var credential = await database.TwoFactorAuthentications.FindAsync(user.Id);
        if (credential?.Enabled == true)
            throw Error("Two-factor authentication is already enabled.");

        if (credential == null)
        {
            credential = new TwoFactorAuthentication { UserId = user.Id };
            await database.TwoFactorAuthentications.AddAsync(credential);
        }

        // Starting setup again must not reset account-wide throttling.
        var secret = Base32Encoding.ToString(RandomNumberGenerator.GetBytes(20));
        credential.ProtectedSecret = protector.Protect(secret);
        credential.SetupExpiresAt = UtcNow.AddMinutes(20);
        credential.LastUsedTimeStep = null;
        credential.KeyVersion = Guid.NewGuid();
        await SaveCredential(credential);

        var uri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email)}" +
            $"?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits=6&period=30";
        using var qr = QRCodeGenerator.GenerateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        using var png = new PngByteQRCode(qr);
        return new TwoFactorSetupDTO
        {
            Secret = secret,
            QrCodeDataUrl = "data:image/png;base64," + Convert.ToBase64String(png.GetGraphic(6)),
        };
    }

    public async Task<List<string>?> Enable(long userId, Session session, string code)
    {
        var credential = await database.TwoFactorAuthentications.FindAsync(userId);
        if (credential == null || credential.Enabled || credential.SetupExpiresAt <= UtcNow ||
            credential.SetupExpiresAt == null)
        {
            throw Error("Start authenticator setup again; this setup has expired or is already complete.");
        }

        if (!await VerifyCode(credential, code, false))
            return null;

        credential.Enabled = true;
        credential.SetupExpiresAt = null;
        var codes = CreateBackupCodes(credential);
        var trackedSession = await database.Sessions.FirstAsync(s => s.Id == session.Id && s.UserId == userId);
        trackedSession.TwoFactorVerifiedAt = UtcNow;
        await RevokeOtherSessions(userId, session.Id);
        await SaveCredential(credential);
        return codes;
    }

    /// <summary>
    ///   Consumes an authenticator step or backup code exactly once and cancels pending recovery on success.
    /// </summary>
    public async Task<bool> VerifyCode(TwoFactorAuthentication credential, string code, bool allowBackup = true)
    {
        if (credential.LockedUntil > UtcNow || credential.ProtectedSecret == null)
            return false;

        var normalized = code.Replace(" ", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
        bool valid = false;
        if (normalized.Length == 6 && normalized.All(c => c is >= '0' and <= '9'))
        {
            var totp = new Totp(Base32Encoding.ToBytes(protector.Unprotect(credential.ProtectedSecret)));
            if (totp.VerifyTotp(UtcNow, normalized, out var step, new VerificationWindow(2, 2)) &&
                (credential.LastUsedTimeStep == null || step > credential.LastUsedTimeStep))
            {
                credential.LastUsedTimeStep = step;
                valid = true;
            }
        }
        else if (allowBackup && credential.Enabled && normalized.Length == 20)
        {
            var hash = HashBackupCode(credential.UserId, normalized);
            var hashes = GetBackupCodeHashes(credential);
            var match = hashes.FindIndex(h =>
                SecurityHelpers.SlowEquals(Convert.FromHexString(h), Convert.FromHexString(hash)));
            if (match >= 0)
            {
                hashes.RemoveAt(match);
                credential.BackupCodeHashes = JsonSerializer.Serialize(hashes);
                valid = true;
            }
        }

        if (valid)
        {
            credential.FailedAttempts = 0;
            credential.LockedUntil = null;
            credential.RecoveryRequestedAt = null;
            credential.RecoveryWarningsSent = 0;
            credential.LastRecoveryWarningAt = null;
        }
        else
        {
            ++credential.FailedAttempts;
            if (credential.FailedAttempts >= 5)
            {
                credential.LockedUntil = UtcNow.AddMinutes(10);
                credential.FailedAttempts = 0;
            }
        }

        await SaveCredential(credential);
        return valid;
    }

    public async Task<bool> Disable(long userId, Session session, string code)
    {
        var credential = await GetEnabledCredential(userId);
        if (!await VerifyCode(credential, code))
            return false;

        ClearCredential(credential);

        // On disabling, we definitely don't need to destroy all sessions
        // await RevokeOtherSessions(userId, session.Id);

        await SaveCredential(credential);
        return true;
    }

    public async Task<List<string>?> RegenerateBackupCodes(long userId, string code)
    {
        var credential = await GetEnabledCredential(userId);
        if (!await VerifyCode(credential, code))
            return null;

        var codes = CreateBackupCodes(credential);
        await SaveCredential(credential);
        return codes;
    }

    public async Task<DateTime> RequestRecovery(long userId)
    {
        if (!mail.Configured)
            throw Error("Email delivery must be configured before two-factor recovery can be requested.");

        var credential = await GetEnabledCredential(userId);
        if (credential.RecoveryRequestedAt != null)
            return credential.RecoveryRequestedAt.Value + RecoveryDelay;

        credential.RecoveryRequestedAt = UtcNow;
        credential.RecoveryWarningsSent = 0;
        credential.LastRecoveryWarningAt = null;
        await SaveCredential(credential);

        // The recurring job also picks this up if the process stops before the enqueue completes.
        jobs.Enqueue<TwoFactorRecoveryJob>(j => j.Execute(CancellationToken.None));
        return UtcNow + RecoveryDelay;
    }

    public async Task SaveCredential(TwoFactorAuthentication credential)
    {
        credential.Revision = Guid.NewGuid();
        try
        {
            await database.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Error("Two-factor state changed during this request. Please refresh and try again.");
        }
    }

    public async Task RevokeOtherSessions(long userId, Guid? keepSession = null)
    {
        var sessions = await database.Sessions.Where(s =>
            (s.UserId == userId || s.PendingTwoFactorUserId == userId) && s.Id != keepSession).ToListAsync();
        database.Sessions.RemoveRange(sessions);
    }

    public void ClearCredential(TwoFactorAuthentication credential)
    {
        credential.Enabled = false;
        credential.ProtectedSecret = null;
        credential.SetupExpiresAt = null;
        credential.BackupCodeHashes = "[]";
        credential.LastUsedTimeStep = null;
        credential.FailedAttempts = 0;
        credential.LockedUntil = null;
        credential.RecoveryRequestedAt = null;
        credential.RecoveryWarningsSent = 0;
        credential.LastRecoveryWarningAt = null;
        credential.KeyVersion = Guid.NewGuid();
    }

    private static HttpResponseException Error(string message)
    {
        return new HttpResponseException { Value = message };
    }

    private static string HashBackupCode(long userId, string code)
    {
        return Convert.ToHexString(SHA3_256.HashData(Encoding.UTF8.GetBytes($"{userId}:{code}")));
    }

    private static List<string> CreateBackupCodes(TwoFactorAuthentication credential)
    {
        var codes = Enumerable.Range(0, 10).Select(_ => RandomNumberGenerator.GetHexString(20)).ToList();
        credential.BackupCodeHashes = JsonSerializer.Serialize(codes.Select(c => HashBackupCode(credential.UserId, c)));
        return codes.Select(c => string.Join('-', Enumerable.Range(0, 4).Select(i => c.Substring(i * 5, 5)))).ToList();
    }

    private async Task<TwoFactorAuthentication> GetEnabledCredential(long userId)
    {
        var credential = await database.TwoFactorAuthentications.FindAsync(userId);
        if (credential?.Enabled != true)
            throw Error("Two-factor authentication is not enabled.");

        return credential;
    }
}
