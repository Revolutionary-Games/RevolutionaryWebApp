namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;
using Shared.Models;
using Utilities;

/// <summary>
///   Reminds the team about open suggestions for the original text in translation projects.
/// </summary>
[DisableConcurrentExecution(500)]
public class ReportSourceTranslationSuggestionsJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IConfiguration configuration;
    private readonly IBackgroundJobClient jobs;

    public ReportSourceTranslationSuggestionsJob(ApplicationDbContext database, IConfiguration configuration,
        IBackgroundJobClient jobs)
    {
        this.database = database;
        this.configuration = configuration;
        this.jobs = jobs;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        // Skip if not configured
        if (string.IsNullOrWhiteSpace(configuration["Discord:TranslationSourceSuggestions"]))
            return;

        var pending = await database.TranslationSuggestions.AsNoTracking()
            .Where(suggestion => suggestion.Status == TranslationSuggestionStatus.Open &&
                suggestion.Value.Unit.Active && suggestion.Value.Unit.Project.Enabled &&
                suggestion.Value.Language.Enabled &&
                suggestion.LanguageCode == suggestion.Value.Unit.Project.SourceLanguage)
            .GroupBy(suggestion => new
            {
                suggestion.ProjectId,
                suggestion.Value.Unit.Project.Name,
                suggestion.Value.Unit.Project.Slug,
                suggestion.LanguageCode,
            })
            .Select(group => new
            {
                group.Key.ProjectId,
                group.Key.Name,
                group.Key.Slug,
                group.Key.LanguageCode,
                Count = group.Count(),
                FirstKey = group.OrderBy(suggestion => suggestion.Value.Unit.Position)
                    .ThenBy(suggestion => suggestion.Key).Select(suggestion => suggestion.Key).First(),
            })
            .OrderBy(project => project.Name).ThenBy(project => project.ProjectId)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return;

        var baseUrl = configuration.GetBaseUrl();
        var message = $"Open source-language suggestions: {pending.Sum(project => project.Count)} " +
            $"across {pending.Count} project(s).\n" + string.Join("\n", pending.Select(project =>
                $"- {project.Name}: {project.Count} — " +
                $"{baseUrl}translations/{Uri.EscapeDataString(project.Slug)}/" +
                $"{Uri.EscapeDataString(project.LanguageCode)}/edit?key={Uri.EscapeDataString(project.FirstKey)}"));
        jobs.Enqueue<SendDiscordWebhookMessageJob>(job =>
            job.Execute("TranslationSourceSuggestions", message, CancellationToken.None));
    }
}
