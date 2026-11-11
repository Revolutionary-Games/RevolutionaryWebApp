namespace RevolutionaryWebApp.Shared.Utilities;

using System;
using System.Globalization;
using System.Linq;

/// <summary>
///   Builds repository links from relative source references without interpreting file names as URL syntax.
/// </summary>
public static class TranslationSourceLinks
{
    public const string DefaultTemplate = "{path}#L{line}";

    public static bool IsValidBaseUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0 &&
            uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
            value.EndsWith("/", StringComparison.Ordinal);
    }

    public static bool IsValidTemplate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (!value.StartsWith("{path}", StringComparison.Ordinal) ||
            (value.Length > 6 && value[6] is not '#' and not '?'))
        {
            return false;
        }

        var suffix = value[6..].Replace("{line}", string.Empty, StringComparison.Ordinal);
        return !suffix.Contains('{') && !suffix.Contains('}');
    }

    /// <summary>
    ///   Returns null for unconfigured or invalid references. References without a line link directly to the file.
    /// </summary>
    public static string? CreateUrl(string? baseUrl, string? template, string location)
    {
        if (!IsValidBaseUrl(baseUrl) || !IsValidTemplate(template))
            return null;

        var path = location.Trim();
        string? line = null;
        var separator = path.LastIndexOf(':');
        if (separator >= 0)
        {
            if (!int.TryParse(path.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture,
                    out var lineNumber) || lineNumber <= 0)
            {
                return null;
            }

            line = lineNumber.ToString(CultureInfo.InvariantCulture);
            path = path[..separator];
        }

        if (path.Length == 0 || path.StartsWith('/') || path.Contains('\\') || path.Contains(':'))
            return null;

        var escapedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        var pattern = string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template;
        if (line == null)
            pattern = "{path}";

        var relativeUrl = pattern.Replace("{path}", escapedPath, StringComparison.Ordinal)
            .Replace("{line}", line ?? string.Empty, StringComparison.Ordinal);
        return Uri.TryCreate(new Uri(baseUrl!), relativeUrl, out var result) ? result.AbsoluteUri : null;
    }
}
