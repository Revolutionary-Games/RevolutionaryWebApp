namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Jobs;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Server.Models.Translations;
using Xunit;

public class ReportPendingDictionaryRequestsJobTests
{
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com")]
    public async Task ReportsPendingCountsAndLinksForEachDictionary(string baseUrl)
    {
        await using var database = CreateDatabase();
        var first = new TranslationDictionary { Id = 1, Name = "First" };
        var second = new TranslationDictionary { Id = 2, Name = "Second" };
        var deleted = new TranslationDictionary { Id = 3, Name = "Deleted", Deleted = true };
        database.TranslationDictionaries.AddRange(first, second, deleted);
        database.DictionaryTermRequests.AddRange(
            new DictionaryTermRequest { Dictionary = second, Word = "Gamma" },
            new DictionaryTermRequest { Dictionary = first, Word = "Alpha" },
            new DictionaryTermRequest { Dictionary = first, Word = "Beta" },
            new DictionaryTermRequest { Dictionary = first, Word = "Accepted", Approved = true },
            new DictionaryTermRequest { Dictionary = second, Word = "Rejected", Approved = false },
            new DictionaryTermRequest { Dictionary = deleted, Word = "Hidden" });
        await database.SaveChangesAsync();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var job = new ReportPendingDictionaryRequestsJob(database, Configuration(baseUrl, "configured"), jobs);

        await job.Execute(CancellationToken.None);

        jobs.Received(1).Create(Arg.Is<Job>(queued =>
            queued.Type == typeof(SendDiscordWebhookMessageJob) &&
            (string)queued.Args[0] == "TranslationRequests" &&
            (string)queued.Args[1] == "Pending dictionary entry requests: 3\n" +
            "- First: 2 — https://example.com/translations/dictionaries/1\n" +
            "- Second: 1 — https://example.com/translations/dictionaries/2\n" +
            "Review: https://example.com/translations/dictionaryRequests"), Arg.Any<EnqueuedState>());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("deleted-dictionary")]
    public async Task DoesNotReportWhenNoRequestsNeedReview(string reason)
    {
        await using var database = CreateDatabase();
        var dictionary = new TranslationDictionary { Name = "Test", Deleted = reason == "deleted-dictionary" };
        database.TranslationDictionaries.Add(dictionary);
        if (reason != "empty")
        {
            database.DictionaryTermRequests.Add(new DictionaryTermRequest
            {
                Dictionary = dictionary, Word = "Test",
                Approved = reason == "deleted-dictionary" ? null : reason == "accepted",
            });
        }

        await database.SaveChangesAsync();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var job = new ReportPendingDictionaryRequestsJob(database,
            Configuration("https://example.com/", "configured"), jobs);

        await job.Execute(CancellationToken.None);

        jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task DoesNotQueueMessagesWithoutWebhook(string? webhook)
    {
        await using var database = CreateDatabase();
        var dictionary = new TranslationDictionary { Name = "Test" };
        database.TranslationDictionaries.Add(dictionary);
        database.DictionaryTermRequests.Add(new DictionaryTermRequest { Dictionary = dictionary, Word = "Test" });
        await database.SaveChangesAsync();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var job = new ReportPendingDictionaryRequestsJob(database,
            Configuration("https://example.com/", webhook), jobs);

        await job.Execute(CancellationToken.None);

        jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    private static ApplicationDbContext CreateDatabase()
    {
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    private static IConfiguration Configuration(string baseUrl, string? webhook)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseUrl"] = baseUrl,
            ["Discord:TranslationRequests"] = webhook,
        }).Build();
    }
}
