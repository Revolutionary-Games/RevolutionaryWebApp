namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models;

[DisableConcurrentExecution(500)]
public class PurgeOldTranslationSuggestionsJob : IJob
{
    private readonly ApplicationDbContext database;

    public PurgeOldTranslationSuggestionsJob(ApplicationDbContext database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        var zeroCutoff = DateTime.UtcNow.AddMonths(-6);
        var negativeCutoff = DateTime.UtcNow.AddDays(-7);
        var candidates = await database.TranslationSuggestions
            .Where(s => s.Status == TranslationSuggestionStatus.Open && s.CreatedAt < negativeCutoff)
            .ToListAsync(cancellationToken);

        var ids = candidates.Select(s => s.Id).ToArray();
        var scores = await database.TranslationSuggestionVotes.Where(v => ids.Contains(v.SuggestionId))
            .GroupBy(v => v.SuggestionId)
            .Select(group => new { Id = group.Key, Score = group.Sum(v => v.Value) })
            .ToDictionaryAsync(row => row.Id, row => row.Score, cancellationToken);

        foreach (var suggestion in candidates)
        {
            scores.TryGetValue(suggestion.Id, out var score);
            if ((score <= -1 && suggestion.CreatedAt < negativeCutoff) ||
                (score == 0 && suggestion.CreatedAt < zeroCutoff))
            {
                database.TranslationSuggestions.Remove(suggestion);
            }
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
