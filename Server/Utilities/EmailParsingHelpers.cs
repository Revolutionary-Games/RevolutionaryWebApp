namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Linq;

public static class EmailParsingHelpers
{
    /// <summary>
    ///   Extracts a plain email address from DSN-style recipient fields such as
    ///   "rfc822; user@example.com" or values that include angle brackets or quotes.
    ///   Falls back to returning the original string trimmed when no better parsing is possible.
    /// </summary>
    /// <param name="raw">The raw value from headers like Final-Recipient or X-Failed-Recipients.</param>
    /// <returns>A best-effort extracted email address string.</returns>
    public static string ExtractEmailFromDsn(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var str = raw.Trim();

        // If the field is in the form "type; address" (e.g. "rfc822; user@example.com"),
        // take the part after the first semicolon.
        var semicolon = str.IndexOf(';');
        if (semicolon >= 0 && semicolon + 1 < str.Length)
        {
            str = str[(semicolon + 1)..].Trim();
        }

        // Remove surrounding angle brackets or quotes
        str = str.Trim('<', '>', '"', '\'', ' ');

        // Some servers may include multiple recipients separated by commas; pick the first one
        // that looks like an email address (contains an '@').
        var candidates = str
            .Split([',', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Trim('<', '>', '"', '\''))
            .ToArray();

        foreach (var c in candidates)
        {
            if (c.Contains('@'))
                return c;
        }

        // As a last resort, return the cleaned string
        return str;
    }
}
