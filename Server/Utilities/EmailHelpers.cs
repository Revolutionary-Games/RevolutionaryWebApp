namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevCenterCommunication.Utilities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Models.Emails;
using RevolutionaryWebApp.Shared.Models.Enums;
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

    /// <summary>
    ///   Generates a footer (HTML and plaintext) that includes manage/unsubscribe links based on whether
    ///   the recipient is a known user or not. For anonymous recipients a time-limited token is generated.
    /// </summary>
    /// <param name="database">EF database to resolve user by id/email</param>
    /// <param name="protector">Time-limited data protector for anonymous preference tokens</param>
    /// <param name="configuration">Configuration used to resolve the absolute base URL</param>
    /// <param name="recipientEmail">Target email address</param>
    /// <param name="category">Email category (used for unsubscribe when allowed)</param>
    /// <param name="recipientUserId">Optional known user id for the recipient</param>
    /// <param name="currentHtml">Current HTML body (without footer)</param>
    /// <param name="currentPlain">Current plaintext body (without footer)</param>
    /// <param name="extraMessage">Optional extra message to show in the footer</param>
    /// <param name="cancellationToken">Cancellation</param>
    /// <returns>Tuple of (htmlWithFooter, plainWithFooter)</returns>
    public static async Task<(string Html, string Plain)> GenerateFooterAsync(ApplicationDbContext database,
        ITimeLimitedDataProtector protector, IConfiguration configuration, string recipientEmail,
        EmailReason category, long? recipientUserId, string? currentHtml, string? currentPlain,
        string? extraMessage, CancellationToken cancellationToken)
    {
        var normalized = Normalization.NormalizeEmail(recipientEmail);

        // Try to resolve a user first (id preferred), matching IsAllowedAsync precedence
        User? user = null;
        if (recipientUserId.HasValue)
        {
            user = await database.Users.FirstOrDefaultAsync(u => u.Id == recipientUserId.Value, cancellationToken);
        }

        user ??= await database.Users
            .FirstOrDefaultAsync(u => u.Email == recipientEmail || u.NormalizedEmail == normalized,
                cancellationToken);

        // Build absolute URLs using configured base URL
        var manageUrlRelative = "/email-preferences";
        var unsubscribeUrlRelative = $"/email-preferences?autoUnsubscribe={Uri.EscapeDataString(category.ToString())}";

        var manageUrl = new Uri(configuration.GetBaseUrl(), manageUrlRelative).ToString();
        var unsubscribeUrl = new Uri(configuration.GetBaseUrl(), unsubscribeUrlRelative).ToString();

        var htmlBuilder = new StringBuilder();
        var textBuilder = new StringBuilder();

        htmlBuilder.AppendLine("<hr/>");
        htmlBuilder.Append("<div style=\"color: #444; font-size: 0.9em;\">");
        if (!string.IsNullOrWhiteSpace(extraMessage))
        {
            htmlBuilder.Append("<p>").Append(System.Net.WebUtility.HtmlEncode(extraMessage))
                .Append("</p>");
            textBuilder.AppendLine(extraMessage);
            textBuilder.AppendLine();
        }

        if (user != null)
        {
            // Known user: for HTML, show unsubscribing (when allowed) and manage links on the same line
            if (category.CanUnSubscribe())
            {
                htmlBuilder.Append("<p>")
                    .Append("<a href=\"").Append(unsubscribeUrl)
                    .Append("\">Unsubscribe</a> from these emails. ")
                    .Append("<a href=\"").Append(manageUrl)
                    .Append("\">Manage email preferences</a>.")
                    .Append("</p>");

                // Plain text: keep on separate lines for readability
                textBuilder.AppendLine($"Unsubscribe: {unsubscribeUrl}");
                textBuilder.AppendLine($"Manage email preferences: {manageUrl}");
            }
            else
            {
                htmlBuilder.Append("<p><a href=\"").Append(manageUrl).Append("\">Manage email preferences</a>.</p>");
                textBuilder.AppendLine($"Manage email preferences: {manageUrl}");
            }
        }
        else
        {
            // Anonymous: generate a token and only provide a single manage link with token in the path
            var token = new EmailPreferenceToken(recipientEmail).ToEncodedString(protector);
            var tokenUrl = new Uri(configuration.GetBaseUrl(), $"/email-preferences/{Uri.EscapeDataString(token)}")
                .ToString();

            htmlBuilder.Append("<p><a href=\"").Append(tokenUrl).Append(
                "\">Manage email preferences</a> for this address.</p>");
            textBuilder.AppendLine($"Manage email preferences: {tokenUrl}");
        }

        htmlBuilder.Append("</div>");

        var htmlFooter = htmlBuilder.ToString();
        var textFooter = textBuilder.ToString();

        var finalHtml = (currentHtml ?? string.Empty) + htmlFooter;
        var finalText = (currentPlain ?? string.Empty) + (string.IsNullOrEmpty(currentPlain) ? string.Empty : "\n\n") +
            textFooter;

        return (finalHtml, finalText);
    }
}
