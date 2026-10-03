namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

/// <summary>
///   Reads string resources from a .NET RESX source and one target language file.
/// </summary>
public static class ResxTranslationParser
{
    public static IReadOnlyList<ResxTranslationEntry> Parse(string sourceXml, string targetXml)
    {
        var source = XDocument.Parse(sourceXml, LoadOptions.PreserveWhitespace);
        var target = XDocument.Parse(targetXml, LoadOptions.PreserveWhitespace);
        var targetValues = target.Root?.Elements("data").Where(IsStringResource)
            .ToDictionary(element => (string)element.Attribute("name")!,
                element => (string?)element.Element("value") ?? string.Empty,
                StringComparer.Ordinal) ?? new Dictionary<string, string>();

        return source.Root?.Elements("data").Where(IsStringResource)
            .Select(element =>
            {
                var key = (string)element.Attribute("name")!;
                targetValues.TryGetValue(key, out var translation);
                return new ResxTranslationEntry(key, (string?)element.Element("value") ?? string.Empty,
                    translation ?? string.Empty, (string?)element.Element("comment"));
            }).ToArray() ?? [];
    }

    private static bool IsStringResource(XElement element)
    {
        return element.Attribute("name") != null && element.Attribute("type") == null &&
            element.Attribute("mimetype") == null;
    }
}
