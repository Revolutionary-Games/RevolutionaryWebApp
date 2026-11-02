namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;

/// <summary>
///   Permanently removes translation resources after their 180-day recovery period.
/// </summary>
[DisableConcurrentExecution(1800)]
public class PurgeDeletedTranslationResourcesJob : IJob
{
    private readonly ApplicationDbContext database;

    public PurgeDeletedTranslationResourcesJob(ApplicationDbContext database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-180);
        var projectIds = await database.TranslationProjects.IgnoreQueryFilters()
            .Where(project => (project.Deleted && project.UpdatedAt <= cutoff) ||
                project.Languages.Any(language => language.Deleted && language.UpdatedAt <= cutoff))
            .Select(project => project.Id).OrderBy(id => id).ToListAsync(cancellationToken);

        foreach (var projectId in projectIds)
        {
            await using var projectLock = await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId,
                cancellationToken);

            await using var transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

            var project = await database.TranslationProjects.IgnoreQueryFilters()
                .SingleAsync(item => item.Id == projectId, cancellationToken);
            var removeProject = project.Deleted && project.UpdatedAt <= cutoff;

            var languages = database.TranslationProjectLanguages.IgnoreQueryFilters().Where(language =>
                language.ProjectId == projectId &&
                (removeProject || (language.Deleted && language.UpdatedAt <= cutoff)));
            var codes = await languages.Select(language => language.LanguageCode).ToArrayAsync(cancellationToken);

            // Deleting dependent things
            if (codes.Any())
            {
                await database.TranslationSuggestions.IgnoreQueryFilters().Where(item => item.ProjectId == projectId &&
                    codes.Contains(item.LanguageCode)).ExecuteDeleteAsync(cancellationToken);
                await database.TranslationComments.IgnoreQueryFilters().Where(item => item.ProjectId == projectId &&
                    codes.Contains(item.LanguageCode)).ExecuteDeleteAsync(cancellationToken);
                await database.TranslationChanges.IgnoreQueryFilters().Where(item => item.ProjectId == projectId &&
                    codes.Contains(item.LanguageCode)).ExecuteDeleteAsync(cancellationToken);
                await database.TranslationImportConflicts.IgnoreQueryFilters().Where(item =>
                    item.ProjectId == projectId &&
                    codes.Contains(item.LanguageCode)).ExecuteDeleteAsync(cancellationToken);
                await database.TranslationValues.IgnoreQueryFilters().Where(item => item.ProjectId == projectId &&
                    codes.Contains(item.LanguageCode)).ExecuteDeleteAsync(cancellationToken);
            }

            await languages.ExecuteDeleteAsync(cancellationToken);

            // Then the main project
            if (removeProject)
            {
                await database.TranslationUnits.IgnoreQueryFilters().Where(item => item.ProjectId == projectId)
                    .ExecuteDeleteAsync(cancellationToken);
                await database.TranslationSyncRuns.IgnoreQueryFilters().Where(item => item.ProjectId == projectId)
                    .ExecuteDeleteAsync(cancellationToken);
                await database.TranslationPullRequests.IgnoreQueryFilters().Where(item => item.ProjectId == projectId)
                    .ExecuteDeleteAsync(cancellationToken);
                await database.TranslationProjects.IgnoreQueryFilters().Where(item => item.Id == projectId)
                    .ExecuteDeleteAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            database.ChangeTracker.Clear();
        }

        await using var dictionaryTransaction =
            await database.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var dictionaries = database.TranslationDictionaries.IgnoreQueryFilters()
            .Where(item => item.Deleted && item.UpdatedAt <= cutoff).Select(item => item.Id);

        var terms = database.DictionaryTerms.IgnoreQueryFilters().Where(item =>
            (item.Deleted && item.UpdatedAt <= cutoff) || dictionaries.Contains(item.DictionaryId));
        await database.DictionaryTermTranslations.IgnoreQueryFilters().Where(translation => terms.Any(term =>
                term.DictionaryId == translation.DictionaryId && term.Word == translation.Word &&
                term.Context == translation.Context))
            .ExecuteDeleteAsync(cancellationToken);
        await terms.ExecuteDeleteAsync(cancellationToken);

        await database.DictionaryLanguageTerms.IgnoreQueryFilters().Where(item =>
                (item.Deleted && item.UpdatedAt <= cutoff) || dictionaries.Contains(item.DictionaryId))
            .ExecuteDeleteAsync(cancellationToken);

        await database.DictionaryTermRequests.IgnoreQueryFilters()
            .Where(item => dictionaries.Contains(item.DictionaryId))
            .ExecuteDeleteAsync(cancellationToken);

        // Change references to the dictionary
        await database.TranslationProjects.IgnoreQueryFilters()
            .Where(item => dictionaries.Contains(item.DictionaryId!.Value))
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.DictionaryId, (long?)null),
                cancellationToken);

        // Final delete and commit
        await database.TranslationDictionaries.IgnoreQueryFilters().Where(item => dictionaries.Contains(item.Id))
            .ExecuteDeleteAsync(cancellationToken);
        await dictionaryTransaction.CommitAsync(cancellationToken);
    }
}
