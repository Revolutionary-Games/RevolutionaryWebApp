namespace RevolutionaryWebApp.Shared.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public static class DictionaryWordMatcher
{
    /// <summary>
    ///   Finds whole terms and regular English plurals, excluding plural endings from the matched span.
    /// </summary>
    public static IEnumerable<Match> FindMatches(string sourceText, string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return Enumerable.Empty<Match>();

        const string boundary = @"(?![\p{L}\p{N}])";
        var escaped = Regex.Escape(word);
        var pattern = escaped + boundary;
        var lower = word.ToLowerInvariant();

        if (lower.EndsWith('y') && lower.Length > 1 && char.IsLetter(lower[^2]) &&
            !"aeiou".Contains(lower[^2]))
        {
            pattern += "|" + Regex.Escape(word[..^1]) + "(?=ies" + boundary + ")";
        }
        else if (lower.EndsWith("s", StringComparison.Ordinal) ||
                 lower.EndsWith("x", StringComparison.Ordinal) || lower.EndsWith("z", StringComparison.Ordinal) ||
                 lower.EndsWith("ch", StringComparison.Ordinal) || lower.EndsWith("sh", StringComparison.Ordinal))
        {
            pattern += "|" + escaped + "(?=es" + boundary + ")";
        }
        else if (char.IsLetter(word[^1]))
        {
            pattern += "|" + escaped + "(?=s" + boundary + ")";
        }

        return Regex.Matches(sourceText, @"(?<![\p{L}\p{N}])(?:" + pattern + ")",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }
}
