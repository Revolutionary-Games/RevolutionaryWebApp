namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Models.Translations;
using Shared.Utilities;

/// <summary>
///   Removes redundant dictionary rows without choosing between entries containing user content.
/// </summary>
public class CleanDuplicateDictionaryLanguagesJob : MaintenanceJobBase
{
    public CleanDuplicateDictionaryLanguagesJob(ILogger<CleanDuplicateDictionaryLanguagesJob> logger,
        ApplicationDbContext operationDb, NotificationsEnabledDb operationStatusDb) : base(logger, operationDb,
        operationStatusDb)
    {
    }

    protected override async Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken)
    {
        await using var transaction = Database.Database.IsRelational() ?
            await Database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) :
            null;

        var translations = await Database.DictionaryTermTranslations.ToListAsync(cancellationToken);
        var languageTerms = await Database.DictionaryLanguageTerms.ToListAsync(cancellationToken);
        var redundant = new List<DictionaryTermTranslation>();
        var conflicts = new List<string>();

        foreach (var group in translations.GroupBy(term =>
                     (term.DictionaryId, term.Word, term.Context,
                         Language: DictionaryLanguageCodes.Normalize(term.LanguageCode))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count() < 2)
                continue;

            // Even equal content in two rows is ambiguous: an administrator must choose which edit to keep.
            var populated = group.Where(term => !string.IsNullOrEmpty(term.Text) ||
                !string.IsNullOrEmpty(term.Explanation)).ToArray();
            if (populated.Length > 1)
            {
                conflicts.Add($"dictionary {group.Key.DictionaryId}, language {group.Key.Language}, " +
                    $"word '{group.Key.Word}', context '{group.Key.Context}' " +
                    $"({string.Join(", ", group.Select(term => term.LanguageCode))})");
                continue;
            }

            var keep = populated.SingleOrDefault() ?? group
                .OrderBy(term => term.LanguageCode != group.Key.Language)
                .ThenBy(term => term.LanguageCode, StringComparer.Ordinal).First();
            redundant.AddRange(group.Where(term => term != keep));
        }

        // A language-specific row's Word is itself a translation. Multiple such rows therefore always need review.
        foreach (var group in languageTerms.GroupBy(term =>
                     (term.DictionaryId, term.Word, term.Context,
                         Language: DictionaryLanguageCodes.Normalize(term.LanguageCode))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Count() > 1)
            {
                conflicts.Add($"language-specific dictionary {group.Key.DictionaryId}, " +
                    $"language {group.Key.Language}, word '{group.Key.Word}', context '{group.Key.Context}' " +
                    $"({string.Join(", ", group.Select(term => term.LanguageCode))})");
            }
        }

        if (conflicts.Count > 0)
        {
            throw new InvalidOperationException("No entries were deleted. Multiple entries contain translations " +
                "or explanations for equivalent languages: " + string.Join("; ", conflicts));
        }

        Database.DictionaryTermTranslations.RemoveRange(redundant);
        await Database.SaveChangesAsync(cancellationToken);
        if (transaction != null)
            await transaction.CommitAsync(cancellationToken);

        operationData.ExtendedDescription = $"Deleted {redundant.Count} duplicate dictionary translation row(s)";
        Logger.LogInformation("Dictionary language cleanup deleted {Deleted} duplicate row(s)", redundant.Count);
    }
}
