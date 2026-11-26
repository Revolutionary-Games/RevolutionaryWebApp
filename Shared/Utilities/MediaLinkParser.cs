namespace RevolutionaryWebApp.Shared.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

public static class MediaLinkParser
{
    public const string Prefix = "media:";

    private const string DisableFullSize = ":f:n";
    private const string DestinationPattern = Prefix + @"(?<extension>[\w.]+):(?<id>[a-f0-9-]+)(?::f:(?<fullSize>[yn]))?";

    private static readonly Regex Destination = new(DestinationPattern, RegexOptions.Compiled,
        TimeSpan.FromSeconds(5));

    private static readonly Regex MarkdownImage = new(
        @"\G!\[(?<alt>[^)\]]*)\]\(" + DestinationPattern + @"(?<title>\s*'[^']*')?\)",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(500));

    public static MediaLink? ParseDestination(string destination)
    {
        var match = Destination.Match(destination);
        return match.Success && match.Length == destination.Length ? ReadMatch(match) : null;
    }

    public static MediaLink? ParseMarkdownImageAt(string content, int start)
    {
        var match = MarkdownImage.Match(content, start);
        return match.Success ? ReadMatch(match) : null;
    }

    public static int CountDestinations(string content)
    {
        return Destination.Count(content);
    }

    public static IEnumerable<MediaLink> FindDestinations(string content)
    {
        return Destination.Matches(content).Select(ReadMatch);
    }

    public static string FormatDestination(string extension, Guid globalId, bool allowFullSize = true)
    {
        // Format requires skipping the leading dot, so remove that (if present)
        if (extension.StartsWith('.'))
            extension = extension[1..];

        return $"{Prefix}{extension}:{globalId}{(allowFullSize ? string.Empty : DisableFullSize)}";
    }

    private static MediaLink ReadMatch(Match match)
    {
        var extension = match.Groups["extension"];
        var hasDot = extension.ValueSpan[0] == '.';
        var alt = match.Groups["alt"];
        var title = match.Groups["title"];

        return new MediaLink(hasDot ? extension.Value : "." + extension.Value, match.Groups["id"].Value,
            match.Groups["fullSize"].Value != "n", extension.Index + (hasDot ? 1 : 0), match.Length,
            alt.Success ? alt.Value : null, title.Success ? title.Value : null);
    }
}

/// <summary>
///   An internal media destination, optionally with its surrounding Markdown image data.
///   ExtensionStart points past any leading dot in the source. Title includes the original quotes and spacing.
/// </summary>
public sealed record MediaLink(string Extension, string GlobalId, bool AllowFullSize, int ExtensionStart, int Length,
    string? AltText, string? Title);
