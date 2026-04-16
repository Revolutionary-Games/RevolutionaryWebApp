namespace RevolutionaryWebApp.Server.Utilities;

using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Utilities;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Emails;
using Services;

public static class EmailHelpers
{
    /// <summary>
    ///   Checks if an address is probably a no-reply one
    /// </summary>
    /// <param name="email">The address to check</param>
    /// <returns>True if probably a no-reply address</returns>
    public static bool IsNoReplyAddress(string email)
    {
        // ReSharper disable StringLiteralTypo
        if (email.Contains("noreply") || email.Contains("no-reply"))
            return true;

        // ReSharper restore StringLiteralTypo
        return false;
    }

    /// <summary>
    ///   Determines if sending an email request is allowed by recipient preferences.
    ///   Implements the rule: if a user exists (by id or by email), do NOT consult direct email preferences.
    ///   When no preferences are found for an existing user, defaults allow.
    /// </summary>
    /// <returns>True when allowed to send, false if the person has disabled the given category</returns>
    public static async Task<bool> IsAllowedAsync(ApplicationDbContext database, MailRequest request,
        CancellationToken cancellationToken)
    {
        var normalized = Normalization.NormalizeEmail(request.Recipient);

        EmailPreferences? prefs;

        // 1) Prefer explicit user id, if provided
        if (request.RecipientUserId.HasValue)
        {
            var user = await database.Users
                .Include(u => u.EmailPreferences)
                .FirstOrDefaultAsync(u => u.Id == request.RecipientUserId.Value, cancellationToken);

            if (user != null)
            {
                // If user exists, never fall back to direct email prefs
                prefs = user.EmailPreferences;
                return prefs?.Allows(request.Category) ?? true;
            }
        }

        // 2) Try to find a user by email
        var userByEmail = await database.Users
            .Include(u => u.EmailPreferences)
            .FirstOrDefaultAsync(u => u.Email == request.Recipient || u.NormalizedEmail == normalized,
                cancellationToken);

        if (userByEmail != null)
        {
            prefs = userByEmail.EmailPreferences;

            // If user exists, do not fall back to direct email preferences
            return prefs?.Allows(request.Category) ?? true;
        }

        // 3) No user found, consult direct email preferences
        var directPrefs = await database.DirectEmailPreferences
            .FirstOrDefaultAsync(p => p.Email == request.Recipient || p.NormalizedEmail == normalized,
                cancellationToken);

        if (directPrefs == null)
        {
            // Default allow when no preferences exist
            return true;
        }

        return directPrefs.Allows(request.Category);
    }
}
