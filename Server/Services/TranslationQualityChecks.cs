namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;
using Shared.Models;

/// <summary>
///   Advisory checks only: these never change text, translation state, or publishing eligibility.
/// </summary>
public static partial class TranslationQualityChecks
{
    public static Dictionary<TranslationProblemKind, string> Evaluate(string source, string translation,
        TranslationQualityCheck checks)
    {
        var problems = new Dictionary<TranslationProblemKind, string>();
        if (string.IsNullOrWhiteSpace(translation))
            return problems;

        if (checks.HasFlag(TranslationQualityCheck.CSharpFormatting))
        {
            var missing = FormatArguments(source).Except(FormatArguments(translation)).OrderBy(index => index)
                .ToArray();
            if (missing.Length > 0)
            {
                problems[TranslationProblemKind.MissingFormatPlaceholder] =
                    "Include the missing C# format placeholders: " +
                    string.Join(", ", missing.Take(20).Select(index =>
                        "{" + index.ToString(CultureInfo.InvariantCulture) + "}")) +
                    (missing.Length > 20 ? $" (and {missing.Length - 20} more from the source). " : ". ") +
                    "Keep their argument numbers; you may reorder them to suit the translation.";
            }
        }

        if (checks.HasFlag(TranslationQualityCheck.SentenceConsistency))
        {
            var sourceRunes = source.Trim().EnumerateRunes().ToArray();
            var translatedRunes = translation.Trim().EnumerateRunes().ToArray();

            // Only lowercase letters can be capitalized. Uncased scripts, such as Chinese, have
            // no uppercase equivalent; Georgian Mkhedruli has uppercase equivalents but does not
            // normally use capitals. Titlecase letters already have appropriate capitalization.
            if (sourceRunes.Length > 0 && Rune.IsUpper(sourceRunes[0]) && Rune.IsLower(translatedRunes[0]) &&
                translatedRunes[0].Value is not (>= 0x10D0 and <= 0x10FF))
            {
                problems[TranslationProblemKind.InitialCapitalization] =
                    "Start the translation with a capital letter, as the source starts with a capital letter. " +
                    "Ignore this warning if capitalization is inappropriate in this language.";
            }

            var sourcePunctuation = sourceRunes.Length > 0 &&
                sourceRunes[^1].Value is not
                    ('{' or '}' or '(' or ')' or '[' or ']' or '（' or '）' or '「' or '」' or '%' or '％') &&
                Rune.IsPunctuation(sourceRunes[^1]);
            var translatedPunctuation = translatedRunes[^1].Value is not
                    ('{' or '}' or '(' or ')' or '[' or ']' or '（' or '）' or '「' or '」' or '%' or '％') &&
                Rune.IsPunctuation(translatedRunes[^1]);
            if (sourcePunctuation != translatedPunctuation)
            {
                problems[TranslationProblemKind.EndingPunctuation] = sourcePunctuation ?
                    "End the translation with punctuation, as the source ends with punctuation. " +
                    "Use the punctuation appropriate for this language; it need not match the source symbol." :
                    "Remove the final punctuation, as the source does not end with punctuation. " +
                    "Ignore this warning if the language requires it.";
            }
        }

        if (checks.HasFlag(TranslationQualityCheck.BbCodeTags) && HasBbCodeProblems(source, translation))
        {
            problems[TranslationProblemKind.BbCodeTagSyntax] =
                "Ensure the translation contains the same BBCode tags as the source and that any tags closed in " +
                "the source are closed in the translation.";
        }

        if (checks.HasFlag(TranslationQualityCheck.ThriveBbCode) && HasThriveBbCodeProblems(source, translation))
        {
            problems[TranslationProblemKind.ThriveBbCodeTagSyntax] =
                "Ensure custom Thrive BBCode tags match the source: keep internal keys inside [thrive:input], " +
                "[thrive:constant], and [thrive:icon] unchanged, keep 'type' attributes in [thrive:compound] and " +
                "[thrive:resource] unchanged, and ensure all tags are closed properly.";
        }

        return problems;
    }

    /// <summary>
    ///   Synchronizes loaded Problems against the current Unit and text. The caller holds the project lock
    ///   and saves these changes together with any translation changes.
    /// </summary>
    public static void UpdateProblems(ApplicationDbContext database, TranslationProject project, TranslationValue value)
    {
        var detected = value.Unit.Active && value.LanguageCode != project.SourceLanguage &&
            value.State != TranslationState.NeedsTranslation ?
                Evaluate(value.Unit.SourceText, value.Text, project.QualityChecks) :
                [];

        foreach (var problem in value.Problems.ToArray())
        {
            if (!detected.Remove(problem.Kind, out var message))
            {
                database.TranslationProblems.Remove(problem);
                value.Problems.Remove(problem);
                continue;
            }

            if (problem.Message != message)
            {
                problem.Message = message;
                problem.Ignored = false;
                problem.IgnoredByUserId = null;
                problem.IgnoredByUser = null;
                problem.IgnoredAt = null;
            }
        }

        foreach (var (kind, message) in detected)
        {
            value.Problems.Add(new TranslationProblem
            {
                ProjectId = value.ProjectId, Key = value.Key, LanguageCode = value.LanguageCode,
                Kind = kind, Message = message,
            });
        }
    }

    public static async Task CheckProject(ApplicationDbContext database, TranslationProject project,
        DateTime? changedSince, CancellationToken cancellationToken)
    {
        var candidates = database.TranslationValues.Where(value => value.ProjectId == project.Id &&
            value.Language.Enabled);
        if (changedSince.HasValue)
        {
            var cutoff = changedSince.Value;
            candidates = candidates.Where(value => value.LastEditedAt >= cutoff || value.CreatedAt >= cutoff ||
                value.Unit.SourceUpdatedAt >= cutoff || value.Problems.Any());
        }

        // Also revisit existing warnings so fixes, removed strings, and disabled checks are cleaned up.
        string? lastKey = null;
        string? lastLanguage = null;
        while (true)
        {
            var keyCursor = lastKey;
            var languageCursor = lastLanguage;
            var batch = keyCursor == null ?
                candidates :
                candidates.Where(value =>
                    string.Compare(value.Key, keyCursor) > 0 ||
                    (value.Key == keyCursor && string.Compare(value.LanguageCode, languageCursor) > 0));
            var values = await batch.OrderBy(value => value.Key).ThenBy(value => value.LanguageCode)
                .Take(200).Include(value => value.Unit).Include(value => value.Problems)
                .ToListAsync(cancellationToken);
            foreach (var value in values)
                UpdateProblems(database, project, value);

            await database.SaveChangesAsync(cancellationToken);

            // This job owns its context. Keep tracking bounded when checking a large project.
            database.ChangeTracker.Clear();
            if (values.Count < 200)
                break;

            lastKey = values[^1].Key;
            lastLanguage = values[^1].LanguageCode;
        }
    }

    public static async Task UpdateAfterEdit(ApplicationDbContext database, TranslationProject project,
        TranslationValue value, CancellationToken cancellationToken)
    {
        UpdateProblems(database, project, value);
        if (value.LanguageCode != project.SourceLanguage)
            return;

        var targets = await database.TranslationValues.Where(target => target.ProjectId == project.Id &&
                target.Key == value.Key && target.LanguageCode != project.SourceLanguage)
            .Include(target => target.Unit).Include(target => target.Problems).ToListAsync(cancellationToken);
        foreach (var target in targets)
            UpdateProblems(database, project, target);
    }

    [GeneratedRegex(@"\G\{([0-9]+) *(?:, *-?[0-9]+ *)?(?::[^{}]*)?\}", RegexOptions.CultureInvariant)]
    private static partial Regex FormatPlaceholder();

    private static HashSet<int> FormatArguments(string text)
    {
        var arguments = new HashSet<int>();
        for (var index = 0; index < text.Length; ++index)
        {
            if (text[index] != '{')
                continue;

            if (index + 1 < text.Length && text[index + 1] == '{')
            {
                ++index;
                continue;
            }

            var match = FormatPlaceholder().Match(text, index);
            if (!match.Success)
                continue;

            if (int.TryParse(match.Groups[1].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture,
                    out var argument))
            {
                arguments.Add(argument);
            }

            index += match.Length - 1;
        }

        return arguments;
    }

    [GeneratedRegex(
        @"\[(?:/(?<closeName>[a-zA-Z][a-zA-Z0-9_-]*)\s*|(?<openName>[a-zA-Z][a-zA-Z0-9_-]*)" +
        @"(?<args>=[^\r\n\]]*|\s+[^\r\n\]]*)?)\]", RegexOptions.CultureInvariant)]
    private static partial Regex BbCodeTag();

    private static bool HasBbCodeProblems(string source, string translation)
    {
        var parsedSource = ParseBbCode(source);
        var parsedTranslation = ParseBbCode(translation);

        if (parsedSource.OpenTags.Count != parsedTranslation.OpenTags.Count)
            return true;

        foreach (var (tag, count) in parsedSource.OpenTags)
        {
            if (parsedTranslation.OpenTags.GetValueOrDefault(tag) != count)
                return true;
        }

        if (parsedTranslation.UnmatchedClosingTags > parsedSource.UnmatchedClosingTags)
            return true;

        foreach (var (tagName, sourceClosedCount) in parsedSource.ClosedTags)
        {
            if (parsedTranslation.ClosedTags.GetValueOrDefault(tagName) < sourceClosedCount)
                return true;
        }

        return false;
    }

    private static ParsedBbCode ParseBbCode(string text)
    {
        var openTags = new Dictionary<string, int>(StringComparer.Ordinal);
        var closedTags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var activeOpenCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unmatchedClosingTags = 0;

        foreach (Match match in BbCodeTag().Matches(text))
        {
            var closeGroup = match.Groups["closeName"];
            if (closeGroup.Success)
            {
                var name = closeGroup.Value;
                if (activeOpenCounts.TryGetValue(name, out var activeCount) && activeCount > 0)
                {
                    activeOpenCounts[name] = activeCount - 1;
                    closedTags[name] = closedTags.GetValueOrDefault(name) + 1;
                }
                else
                {
                    ++unmatchedClosingTags;
                }
            }
            else
            {
                var openGroup = match.Groups["openName"];
                var name = openGroup.Value.ToLowerInvariant();
                var args = match.Groups["args"].Value;
                var fullTag = $"[{name}{args}]";

                openTags[fullTag] = openTags.GetValueOrDefault(fullTag) + 1;
                activeOpenCounts[name] = activeOpenCounts.GetValueOrDefault(name) + 1;
            }
        }

        return new ParsedBbCode(openTags, closedTags, unmatchedClosingTags);
    }

    [GeneratedRegex(
        @"\[(?:/(?<closeName>thrive:[a-zA-Z]+)\s*|(?<openName>thrive:[a-zA-Z]+)(?<attrs>[^\]]*)?)\]",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ThriveBbCodeTag();

    [GeneratedRegex(
        @"(?<key>[a-zA-Z0-9_-]+)(?:=(?:""(?<val>[^""]*)""|'(?<val>[^']*)'|(?<val>[^\s\]]+)))?",
        RegexOptions.CultureInvariant)]
    private static partial Regex AttributeKeyValuePair();

    private static bool HasThriveBbCodeProblems(string source, string translation)
    {
        var parsedSource = ParseThriveBbCode(source);
        var parsedTranslation = ParseThriveBbCode(translation);

        if (parsedSource.TotalOpenTags != parsedTranslation.TotalOpenTags)
        {
            return true;
        }

        if (parsedTranslation.UnmatchedClosingTags > parsedSource.UnmatchedClosingTags)
        {
            return true;
        }

        if (parsedSource.ImmutableTags.Count != parsedTranslation.ImmutableTags.Count)
        {
            return true;
        }

        foreach (var (key, count) in parsedSource.ImmutableTags)
        {
            if (parsedTranslation.ImmutableTags.GetValueOrDefault(key) != count)
            {
                return true;
            }
        }

        if (parsedSource.MutableTags.Count != parsedTranslation.MutableTags.Count)
        {
            return true;
        }

        foreach (var (key, count) in parsedSource.MutableTags)
        {
            if (parsedTranslation.MutableTags.GetValueOrDefault(key) != count)
            {
                return true;
            }
        }

        foreach (var (tagName, sourceClosedCount) in parsedSource.ClosedTags)
        {
            if (parsedTranslation.ClosedTags.GetValueOrDefault(tagName) < sourceClosedCount)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMutableContentTag(string tag)
    {
        return tag is "compound" or "resource";
    }

    private static string NormalizeAttributes(string attrs)
    {
        if (string.IsNullOrWhiteSpace(attrs))
        {
            return string.Empty;
        }

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttributeKeyValuePair().Matches(attrs))
        {
            var key = match.Groups["key"].Value.ToLowerInvariant();
            var val = match.Groups["val"].Success ? match.Groups["val"].Value : string.Empty;
            dict[key] = val;
        }

        if (dict.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",", dict.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{p.Key}=\"{p.Value}\""));
    }

    private static ParsedThriveBbCode ParseThriveBbCode(string text)
    {
        var immutableTags = new Dictionary<string, int>(StringComparer.Ordinal);
        var mutableTags = new Dictionary<string, int>(StringComparer.Ordinal);
        var closedTags = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tagStack = new Stack<(string Tag, string Attrs, int ContentStartIndex)>();
        var unmatchedClosingTags = 0;
        var totalOpenTags = 0;

        foreach (Match match in ThriveBbCodeTag().Matches(text))
        {
            var closeGroup = match.Groups["closeName"];
            if (closeGroup.Success)
            {
                var closeTag = closeGroup.Value.Substring("thrive:".Length).ToLowerInvariant();
                if (tagStack.Count > 0 && tagStack.Peek().Tag == closeTag)
                {
                    var (tag, attrs, contentStartIndex) = tagStack.Pop();
                    closedTags[tag] = closedTags.GetValueOrDefault(tag) + 1;

                    var rawContent = text.Substring(contentStartIndex, match.Index - contentStartIndex).Trim();

                    if (IsMutableContentTag(tag))
                    {
                        var key = $"[{tag} {attrs}]";
                        mutableTags[key] = mutableTags.GetValueOrDefault(key) + 1;
                    }
                    else
                    {
                        var key = $"[{tag} {attrs}]{rawContent}";
                        immutableTags[key] = immutableTags.GetValueOrDefault(key) + 1;
                    }
                }
                else
                {
                    ++unmatchedClosingTags;
                }
            }
            else
            {
                var openName = match.Groups["openName"].Value;
                var tag = openName.Substring("thrive:".Length).ToLowerInvariant();
                var attrs = NormalizeAttributes(match.Groups["attrs"].Value);
                var contentStartIndex = match.Index + match.Length;

                tagStack.Push((tag, attrs, contentStartIndex));
                ++totalOpenTags;
            }
        }

        while (tagStack.Count > 0)
        {
            var (tag, attrs, contentStartIndex) = tagStack.Pop();
            var rawContent = text.Substring(contentStartIndex).Trim();
            if (IsMutableContentTag(tag))
            {
                var key = $"[unclosed:{tag} {attrs}]";
                mutableTags[key] = mutableTags.GetValueOrDefault(key) + 1;
            }
            else
            {
                var key = $"[unclosed:{tag} {attrs}]{rawContent}";
                immutableTags[key] = immutableTags.GetValueOrDefault(key) + 1;
            }
        }

        return new ParsedThriveBbCode(immutableTags, mutableTags, closedTags, unmatchedClosingTags, totalOpenTags);
    }

    private readonly record struct ParsedBbCode(
        Dictionary<string, int> OpenTags,
        Dictionary<string, int> ClosedTags,
        int UnmatchedClosingTags);

    private readonly record struct ParsedThriveBbCode(
        Dictionary<string, int> ImmutableTags,
        Dictionary<string, int> MutableTags,
        Dictionary<string, int> ClosedTags,
        int UnmatchedClosingTags,
        int TotalOpenTags);
}
