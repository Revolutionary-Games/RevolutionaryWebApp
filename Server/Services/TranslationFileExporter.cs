namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        IReadOnlyCollection<TranslationValue> values, DateTime? latestChange = null, string? lastTranslator = null)
    {
        var template = ParsePo(templateContent);
        var target = ParsePo(targetContent);
        var byKey = values.ToDictionary(value => value.Key, StringComparer.Ordinal);
        var replacements = new Dictionary<POKey, IPOEntry>();

        foreach (var sourceEntry in template.Values)
        {
            var key = sourceEntry.Key.ContextId == null ?
                sourceEntry.Key.Id :
                sourceEntry.Key.ContextId + "\u0004" + sourceEntry.Key.Id;
            if (!byKey.TryGetValue(key, out var value))
                continue;

            var exists = target.TryGetValue(sourceEntry.Key, out var targetEntry);
            if (!exists)
            {
                if (string.IsNullOrEmpty(value.Text))
                    continue;

                targetEntry = sourceEntry is POPluralEntry ?
                    new POPluralEntry(sourceEntry.Key,
                        Enumerable.Repeat(string.Empty, Math.Max(1, target.PluralFormCount))) :
                    new POSingularEntry(sourceEntry.Key);
            }

            var changed = !exists;
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

            // Empty translations are unused, so fuzzy only has meaning for nonempty text.
            var fuzzy = value.State == TranslationState.NeedsTranslation &&
                (targetEntry is POPluralEntry plurals ?
                    plurals.Any(text => !string.IsNullOrEmpty(text)) :
                    !string.IsNullOrEmpty(value.Text));

            var comments = targetEntry!.Comments ??= new List<POComment>();
            var flags = comments.OfType<POFlagsComment>().ToArray();
            changed |= fuzzy != flags.Any(comment => comment.Flags.Contains("fuzzy"));
            foreach (var flag in flags)
            {
                flag.Flags.Remove("fuzzy");
                if (flag.Flags.Count == 0)
                    comments.Remove(flag);
            }

            if (fuzzy)
            {
                var flag = comments.OfType<POFlagsComment>().FirstOrDefault();
                if (flag == null)
                {
                    flag = new POFlagsComment { Flags = new HashSet<string>(StringComparer.Ordinal) };
                    comments.Add(flag);
                }

                flag.Flags.Add("fuzzy");
            }

            if (changed)
                replacements.Add(sourceEntry.Key, targetEntry);
        }

        if (replacements.Count == 0)
            return targetContent;

        var newline = targetContent.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        // ReSharper disable StringLiteralTypo

        // Match active entries only. Retain all other text, including headers and obsolete entries,
        // verbatim: the PO generator normalizes comments and discards obsolete translations.
        const string quotedLines = "(?:[^\r\n]*)(?:\r?\n\"[^\r\n]*)*";
        var entryPattern = @"(?m)^(?:#(?!~)[^\r\n]*\r?\n)*(?:msgctxt " + quotedLines +
            @"\r?\n)?msgid " + quotedLines + @"\r?\n(?:msgid_plural " + quotedLines +
            @"\r?\n)?msgstr(?:\[\d+\])? " + quotedLines +
            @"(?:\r?\nmsgstr\[\d+\] " + quotedLines + ")*";

        // ReSharper restore StringLiteralTypo

        var output = Regex.Replace(targetContent, entryPattern, match =>
        {
            var catalog = ParsePo(match.Value);
            var entry = catalog.Values.FirstOrDefault();
            if (entry == null)
            {
                var header = match.Value;
                if (latestChange != null)
                {
                    header = UpdatePoHeader(header, "PO-Revision-Date",
                        latestChange.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "+0000",
                        newline);
                }

                if (lastTranslator != null)
                    header = UpdatePoHeader(header, "Last-Translator", lastTranslator, newline);
                return header;
            }

            if (!replacements.Remove(entry.Key, out var replacement))
                return match.Value;

            var text = match.Value;
            var oldTranslation = entry is POSingularEntry single ? single.Translation : ((POPluralEntry)entry)[0];
            var newTranslation = replacement is POSingularEntry updated ?
                updated.Translation :
                ((POPluralEntry)replacement)[0];
            if (oldTranslation != newTranslation)
            {
                var generated = GeneratePoEntry(replacement, newline);
                var translation = Regex.Match(generated, @"(?m)^msgstr[^\r\n]*(?:\r?\n""[^\r\n]*)*");
                text = Regex.Replace(text, @"(?m)^msgstr(?:\[0\])? [^\r\n]*(?:\r?\n""[^\r\n]*)*",
                    _ => translation.Value, RegexOptions.None);
            }

            var fuzzy = replacement.Comments?.OfType<POFlagsComment>()
                .Any(comment => comment.Flags.Contains("fuzzy")) == true;
            var wasFuzzy = entry.Comments?.OfType<POFlagsComment>()
                .Any(comment => comment.Flags.Contains("fuzzy")) == true;
            if (fuzzy == wasFuzzy)
                return text;

            text = Regex.Replace(text, @"(?m)^#,([^\r\n]*)(?:\r?\n|$)", flagMatch =>
            {
                var flags = flagMatch.Groups[1].Value.Split(',').Select(flag => flag.Trim())
                    .Where(flag => flag.Length > 0 && flag != "fuzzy").ToList();
                return flags.Count == 0 ? string.Empty : "#, " + string.Join(", ", flags) + newline;
            });
            if (fuzzy)
                text = "#, fuzzy" + newline + text;
            return text;
        });

        foreach (var entry in replacements.Values)
        {
            if (target.TryGetValue(entry.Key, out _))
                throw new FormatException($"Could not locate gettext entry in original text: {entry.Key}");

            if (output.Length > 0 && !output.EndsWith(newline + newline, StringComparison.Ordinal))
                output += output.EndsWith(newline, StringComparison.Ordinal) ? newline : newline + newline;
            output += GeneratePoEntry(entry, newline) + newline;
        }

        return output;
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
                AddResxEntry(target.Root!, element, source.Root!);
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

                // PreserveWhitespace already retained the gap after the declaration.
                output = targetContent[..declarationEnd] + output;
            }
        }

        if (targetContent.EndsWith('\n') && !output.EndsWith('\n'))
            output += "\n";

        if (targetContent.Contains("\r\n", StringComparison.Ordinal))
        {
            output = output.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace("\n", "\r\n", StringComparison.Ordinal);
        }

        return output;
    }

    private static string UpdatePoHeader(string header, string name, string value, string newline)
    {
        // Escape PO string syntax without changing any other header or its position.
        var escaped = value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);
        var line = "\"" + name + ": " + escaped + "\\n\"";
        var pattern = "(?m)^\"" + Regex.Escape(name) + ":[^\r\n]*";
        return Regex.IsMatch(header, pattern) ?
            Regex.Replace(header, pattern, _ => line) :
            header + newline + line;
    }

    private static string GeneratePoEntry(IPOEntry entry, string newline)
    {
        var catalog = new POCatalog();
        catalog.TryAdd(entry);
        using var writer = new StringWriter();
        writer.NewLine = newline;
        new POGenerator(new POGeneratorSettings
        {
            IgnoreEncoding = true,
            SkipInfoHeaders = true,
            PreserveHeadersOrder = true,
            MaxLineLength = int.MaxValue,
        }).Generate(writer, catalog);
        var output = writer.ToString();

        // The generator always emits a catalog header, even for this single-entry catalog.
        var headerEnd = output.IndexOf(newline + newline, StringComparison.Ordinal);
        if (headerEnd >= 0)
            output = output[(headerEnd + 2 * newline.Length)..];
        return output.TrimEnd('\r', '\n');
    }

    private static void AddResxEntry(XElement root, XElement entry, XElement sourceRoot)
    {
        var sample = root.Elements("data").FirstOrDefault() ?? sourceRoot.Elements("data").FirstOrDefault();
        var indent = GetResxIndent(sample) ?? "  ";
        var valueIndent = GetResxIndent(sample?.Element("value")) ?? indent + "  ";
        entry.AddFirst(new XText("\n" + valueIndent));
        entry.Add(new XText("\n" + indent));

        // Insert before the closing tag's whitespace so it stays on its own line.
        if (root.LastNode is XText trailing && string.IsNullOrWhiteSpace(trailing.Value))
        {
            trailing.AddBeforeSelf(new XText("\n" + indent), entry);
        }
        else
        {
            root.Add(new XText("\n" + indent), entry, new XText("\n"));
        }
    }

    private static string? GetResxIndent(XElement? element)
    {
        if (element?.PreviousNode is not XText whitespace || !string.IsNullOrWhiteSpace(whitespace.Value))
            return null;

        var newline = whitespace.Value.LastIndexOf('\n');
        return newline >= 0 ? whitespace.Value[(newline + 1)..] : null;
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
