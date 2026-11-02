namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;
using Models.Translations;

/// <summary>
///   Initializes existing suggestion scores and repairs counts after votes disappear with deleted users.
/// </summary>
[DisableConcurrentExecution(1800)]
public class RecountTranslationSuggestionScoresJob : IJob
{
    private readonly ApplicationDbContext database;
    public RecountTranslationSuggestionScoresJob(ApplicationDbContext database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var projects = await database.TranslationSuggestions.IgnoreQueryFilters().Select(item => item.ProjectId)
            .Distinct().OrderBy(id => id).ToListAsync(cancellationToken);
        foreach (var projectId in projects)
        {
            await using var projectLock = await TranslationProjectAdvisoryLock.AcquireAsync(database, projectId,
                cancellationToken);
            await database.TranslationSuggestions.IgnoreQueryFilters().Where(item => item.ProjectId == projectId)
                .ExecuteUpdateAsync(update => update.SetProperty(item => item.Score,
                    item => database.TranslationSuggestionVotes.IgnoreQueryFilters()
                        .Where(vote => vote.SuggestionId == item.Id)
                        .Sum(vote => (int?)vote.Value) ?? 0), cancellationToken);
        }
    }
}
