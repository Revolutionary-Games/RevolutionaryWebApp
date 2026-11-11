namespace RevolutionaryWebApp.Server.Jobs.RegularlyScheduled;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models;

[DisableConcurrentExecution(1000)]
public class ReportPendingDictionaryRequestsJob : IJob
{
    private readonly ApplicationDbContext database;
    private readonly IConfiguration configuration;
    private readonly IBackgroundJobClient jobs;

    public ReportPendingDictionaryRequestsJob(ApplicationDbContext database, IConfiguration configuration,
        IBackgroundJobClient jobs)
    {
        this.database = database;
        this.configuration = configuration;
        this.jobs = jobs;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["Discord:TranslationRequests"]))
            return;

        var pending = await database.DictionaryTermRequests.AsNoTracking()
            .Where(request => request.Approved == null)
            .GroupBy(request => new { request.DictionaryId, request.Dictionary.Name })
            .Select(group => new { group.Key.DictionaryId, group.Key.Name, Count = group.Count() })
            .OrderBy(dictionary => dictionary.Name).ThenBy(dictionary => dictionary.DictionaryId)
            .ToListAsync(cancellationToken);
        if (pending.Count == 0)
            return;

        var baseUrl = configuration["BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var message = $"Pending dictionary entry requests: {pending.Sum(dictionary => dictionary.Count)}\n" +
            string.Join("\n",
                pending.Select(dictionary => $"- {dictionary.Name}: {dictionary.Count} — " +
                    $"{baseUrl}/translations/dictionaries/{dictionary.DictionaryId}")) +
            $"\nReview: {baseUrl}/translations/dictionaryRequests";
        jobs.Enqueue<SendDiscordWebhookMessageJob>(job =>
            job.Execute("TranslationRequests", message, CancellationToken.None));
    }
}
