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
            if (sourceRunes.Length > 0 && Rune.IsUpper(sourceRunes[0]) && !Rune.IsUpper(translatedRunes[0]))
            {
                problems[TranslationProblemKind.InitialCapitalization] =
                    "Start the translation with a capital letter, as the source starts with a capital letter. " +
                    "Ignore this warning if capitalization is inappropriate in this language.";
            }

            var sourcePunctuation = sourceRunes.Length > 0 && Rune.IsPunctuation(sourceRunes[^1]);
            var translatedPunctuation = Rune.IsPunctuation(translatedRunes[^1]);
            if (sourcePunctuation != translatedPunctuation)
            {
                problems[TranslationProblemKind.EndingPunctuation] = sourcePunctuation ?
                    "End the translation with punctuation, as the source ends with punctuation. " +
                    "Use the punctuation appropriate for this language; it need not match the source symbol." :
                    "Remove the final punctuation, as the source does not end with punctuation. " +
                    "Ignore this warning if the language requires it.";
            }
        }

        return problems;
    }

    /// <summary>
    ///   Synchronizes loaded Problems against the current Unit and text. The caller holds the project lock
    ///   and saves these changes together with any translation changes.
    /// </summary>
    public static void UpdateProblems(ApplicationDbContext database, TranslationProject project, TranslationValue value)
    {
        var detected = value.Unit.Active && value.LanguageCode != project.SourceLanguage ?
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
}
