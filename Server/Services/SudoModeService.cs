namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Models;
using Shared;

/// <summary>
///   Grants sudo access to one browser session, independently of login and ordinary two-factor verification.
/// </summary>
public class SudoModeService(NotificationsEnabledDb database, TwoFactorService twoFactor, IDistributedCache cache)
{
    public async Task<bool> IsActive(Session session, long userId)
    {
        if (session.UserId != userId || session.PendingTwoFactorUserId != null)
            return false;

        // Query the current credential so disabling or replacing two-factor immediately invalidates sudo grants.
        var credential = await database.TwoFactorAuthentications.AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.Enabled);
        if (credential == null)
            return false;

        var expires = await cache.GetStringAsync(CacheKey(session, credential));
        return long.TryParse(expires, CultureInfo.InvariantCulture, out var ticks) && ticks > twoFactor.UtcNow.Ticks;
    }

    public async Task<bool> Enter(Session session, long userId, string code)
    {
        if (session.UserId != userId || session.PendingTwoFactorUserId != null)
            return false;

        var credential = await database.TwoFactorAuthentications.FindAsync(userId);
        if (credential?.Enabled != true || !await twoFactor.VerifyCode(credential, code))
            return false;

        var duration = TimeSpan.FromMinutes(AppInfo.SudoModeDurationMinutes);
        var expires = twoFactor.UtcNow + duration;
        await cache.SetStringAsync(CacheKey(session, credential), expires.Ticks.ToString(CultureInfo.InvariantCulture),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = duration });
        return true;
    }

    private static string CacheKey(Session session, TwoFactorAuthentication credential)
    {
        return $"sudo:{credential.UserId}:{session.Id}:{credential.KeyVersion}";
    }
}
