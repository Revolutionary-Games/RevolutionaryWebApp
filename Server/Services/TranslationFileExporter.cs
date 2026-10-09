namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Karambolo.PO;
using Models.Translations;
using Shared.Models;

/// <summary>
///   Applies current database values to a freshly fetched repository file.
/// </summary>
public static class TranslationFileExporter
{
    public static string ExportGettext(string templateContent, string targetContent,
        IReadOnlyCollection<TranslationValue> values)
    {
        var template = ParsePo(templateContent);
        var target = ParsePo(targetContent);
        var byKey = values.ToDictionary(value => value.Key, StringComparer.Ordinal);
        var changed = false;

        foreach (var sourceEntry in template.Values)
        {
            var key = sourceEntry.Key.ContextId == null ?
                sourceEntry.Key.Id :
                sourceEntry.Key.ContextId + "\u0004" + sourceEntry.Key.Id;
            if (!byKey.TryGetValue(key, out var value))
                continue;

            if (!target.TryGetValue(sourceEntry.Key, out var targetEntry))
            {
                if (string.IsNullOrEmpty(value.Text))
                    continue;

                targetEntry = sourceEntry is POPluralEntry ?
                    new POPluralEntry(sourceEntry.Key,
                        Enumerable.Repeat(string.Empty, Math.Max(1, target.PluralFormCount))) :
                    new POSingularEntry(sourceEntry.Key);
                target.TryAdd(targetEntry);
                changed = true;
            }

            if (targetEntry is POSingularEntry singular)
            {
                changed |= singular.Translation != value.Text;
                singular.Translation = value.Text;
            }
            else if (targetEntry is POPluralEntry plural)
            {
                changed |= plural[0] != value.Text;
                plural[0] = value.Text;
            }

            var comments = targetEntry.Comments ??= new List<POComment>();
            var flags = comments.OfType<POFlagsComment>().FirstOrDefault();
            if (value.State == TranslationState.NeedsTranslation)
            {
                changed |= flags?.Flags.Contains("fuzzy") != true;
                if (flags == null)
                {
                    flags = new POFlagsComment { Flags = new HashSet<string>(StringComparer.Ordinal) };
                    comments.Add(flags);
                }

                flags.Flags.Add("fuzzy");
            }
            else
            {
                changed |= flags?.Flags.Contains("fuzzy") == true;
                flags?.Flags.Remove("fuzzy");
            }
        }

        if (!changed)
            return targetContent;

        using var writer = new StringWriter();
        new POGenerator(new POGeneratorSettings
        {
            IgnoreEncoding = true,
            PreserveHeadersOrder = true,
            MaxLineLength = int.MaxValue,
        }).Generate(writer, target);
        return writer.ToString();
    }

    public static string ExportResx(string sourceContent, string targetContent,
        IReadOnlyCollection<TranslationValue> values)
    {
        var source = XDocument.Parse(sourceContent, LoadOptions.PreserveWhitespace);
        var target = XDocument.Parse(targetContent, LoadOptions.PreserveWhitespace);
        var byKey = values.ToDictionary(value => value.Key, StringComparer.Ordinal);
        var existing = target.Root?.Elements("data").Where(e => e.Attribute("name") != null)
                .ToDictionary(e => (string)e.Attribute("name")!, StringComparer.Ordinal) ??
            new Dictionary<string, XElement>();
        var changed = false;

        foreach (var sourceEntry in source.Root?.Elements("data") ?? [])
        {
            var key = (string?)sourceEntry.Attribute("name");
            if (key == null || !byKey.TryGetValue(key, out var value))
                continue;

            if (!existing.TryGetValue(key, out var element))
            {
                if (string.IsNullOrEmpty(value.Text))
                    continue;

                element = new XElement("data", new XAttribute("name", key),
                    new XAttribute(XNamespace.Xml + "space", "preserve"), new XElement("value"));
                target.Root?.Add(element);
                changed = true;
            }

            var textElement = element.Element("value");
            if (textElement == null)
                element.Add(textElement = new XElement("value"));
            changed |= textElement.Value != value.Text;
            textElement.Value = value.Text;
        }

        if (!changed)
            return targetContent;

        var output = target.ToString(SaveOptions.DisableFormatting);
        var declarationStart = targetContent.IndexOf("<?xml", StringComparison.Ordinal);
        if (declarationStart == 0)
        {
            var declarationEnd = targetContent.IndexOf("?>", StringComparison.Ordinal);
            if (declarationEnd >= 0)
            {
                declarationEnd += 2;
                var gap = new string(targetContent[declarationEnd..].TakeWhile(char.IsWhiteSpace).ToArray());
                output = targetContent[..declarationEnd] + gap + output;
            }
        }

        if (targetContent.EndsWith('\n') && !output.EndsWith('\n'))
            output += "\n";

        return output;
    }

    private static POCatalog ParsePo(string content)
    {
        using var reader = new StringReader(content);
        var result = new POParser().Parse(reader);
        if (!result.Success || result.Catalog == null)
            throw new FormatException($"Invalid gettext catalog: {result.Diagnostics}");

        return result.Catalog;
    }
}
