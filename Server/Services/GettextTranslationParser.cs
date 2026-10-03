namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Karambolo.PO;

/// <summary>
///   Reads a gettext template, English catalog, and one target catalog for a symbolic-key project.
/// </summary>
public static class GettextTranslationParser
{
    public static IReadOnlyList<GettextTranslationEntry> Parse(string templateContent, string sourceContent,
        string targetContent)
    {
        var template = ParseCatalog(templateContent);
        var source = ParseCatalog(sourceContent);
        var target = ParseCatalog(targetContent);
        var sourceByKey = source.Values.ToDictionary(entry => entry.Key);
        var targetByKey = target.Values.ToDictionary(entry => entry.Key);
        var output = new List<GettextTranslationEntry>();

        foreach (var templateEntry in template.Values)
        {
            if (string.IsNullOrEmpty(templateEntry.Key.Id))
                continue;

            sourceByKey.TryGetValue(templateEntry.Key, out var sourceEntry);
            targetByKey.TryGetValue(templateEntry.Key, out var targetEntry);

            var refs = templateEntry.Comments?.OfType<POReferenceComment>()
                .SelectMany(comment => comment.References)
                .Select(reference => reference.ToString()).Distinct().ToArray() ?? [];
            var description = templateEntry.Comments?.OfType<POExtractedComment>()
                .Select(comment => comment.Text).FirstOrDefault();
            var fuzzy = targetEntry?.Comments?.OfType<POFlagsComment>()
                .Any(comment => comment.Flags.Contains("fuzzy")) ?? false;
            var sourceText = TextOf(sourceEntry);
            var targetText = TextOf(targetEntry);
            var plurals = targetEntry is POPluralEntry pluralEntry ? pluralEntry.ToArray() : [];

            var key = templateEntry.Key.ContextId == null ? templateEntry.Key.Id :
                templateEntry.Key.ContextId + "\u0004" + templateEntry.Key.Id;

            output.Add(new GettextTranslationEntry(key, sourceText, targetText,
                templateEntry.Key.ContextId, templateEntry.Key.PluralId, refs, description, fuzzy, plurals));
        }

        return output;
    }

    private static POCatalog ParseCatalog(string content)
    {
        using var reader = new StringReader(content);
        var result = new POParser().Parse(reader);
        if (!result.Success || result.Catalog == null)
            throw new FormatException($"Invalid gettext catalog: {result.Diagnostics}");

        return result.Catalog;
    }

    private static string TextOf(IPOEntry? entry)
    {
        return entry switch
        {
            POSingularEntry singular => singular.Translation,
            POPluralEntry plural => plural.FirstOrDefault() ?? string.Empty,
            _ => string.Empty,
        };
    }
}
