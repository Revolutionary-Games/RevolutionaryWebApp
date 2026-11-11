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
using Shared.Models;
using Xunit;

public class ReportSourceTranslationSuggestionsJobTests
{
    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://example.com")]
    public async Task ReportsTotalAndEachProjectWithItsFirstText(string baseUrl)
    {
        await using var database = CreateDatabase();
        var first = AddProject(database, 1, "First", "first", "en");
        var second = AddProject(database, 2, "Second", "second", "de");
        AddSuggestion(database, first, "en", "LAST", 10);
        AddSuggestion(database, first, "en", "A & ?/#", 0);
        AddSuggestion(database, first, "en", "A & ?/#", 0);
        AddSuggestion(database, first, "en", "Z", 0);
        AddSuggestion(database, second, "de", "START", 0);
        AddSuggestion(database, second, "en", "TARGET", 0);
        await database.SaveChangesAsync();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var job = new ReportSourceTranslationSuggestionsJob(database, Configuration(baseUrl, "configured"), jobs);

        await job.Execute(CancellationToken.None);

        jobs.Received(1).Create(Arg.Is<Job>(queued =>
            queued.Type == typeof(SendDiscordWebhookMessageJob) &&
            (string)queued.Args[0] == "TranslationSourceSuggestions" &&
            (string)queued.Args[1] == "Open source-language suggestions: 5 across 2 project(s).\n" +
            "- First: 4 — https://example.com/translations/first/en/edit?key=A%20%26%20%3F%2F%23\n" +
            "- Second: 1 — https://example.com/translations/second/de/edit?key=START"), Arg.Any<EnqueuedState>());
    }

    [Theory]
    [InlineData("accepted")]
    [InlineData("rejected")]
    [InlineData("target")]
    [InlineData("inactive")]
    [InlineData("deleted-project")]
    [InlineData("disabled-project")]
    [InlineData("deleted-language")]
    [InlineData("disabled-language")]
    [InlineData("empty")]
    public async Task DoesNotReportSuggestionsThatCannotBeReviewed(string reason)
    {
        await using var database = CreateDatabase();
        var project = AddProject(database, 1, "Test", "test", "en");
        if (reason != "empty")
        {
            var suggestion = AddSuggestion(database, project, reason == "target" ? "fi" : "en", "TEXT", 0);
            switch (reason)
            {
                case "accepted":
                    suggestion.Status = TranslationSuggestionStatus.Accepted;
                    break;
                case "rejected":
                    suggestion.Status = TranslationSuggestionStatus.Rejected;
                    break;
                case "inactive":
                    suggestion.Value.Unit.Active = false;
                    break;
                case "deleted-project":
                    project.Deleted = true;
                    break;
                case "disabled-project":
                    project.Enabled = false;
                    break;
                case "deleted-language":
                    suggestion.Value.Language.Deleted = true;
                    break;
                case "disabled-language":
                    suggestion.Value.Language.Enabled = false;
                    break;
            }
        }

        await database.SaveChangesAsync();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var job = new ReportSourceTranslationSuggestionsJob(database,
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
        var project = AddProject(database, 1, "Test", "test", "en");
        AddSuggestion(database, project, "en", "TEXT", 0);
        await database.SaveChangesAsync();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var job = new ReportSourceTranslationSuggestionsJob(database,
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
            ["Discord:TranslationSourceSuggestions"] = webhook,
        }).Build();
    }

    private static TranslationProject AddProject(ApplicationDbContext database, long id, string name, string slug,
        string sourceLanguage)
    {
        var project = new TranslationProject { Id = id, Name = name, Slug = slug, SourceLanguage = sourceLanguage };
        database.TranslationProjects.Add(project);
        return project;
    }

    private static TranslationSuggestion AddSuggestion(ApplicationDbContext database, TranslationProject project,
        string languageCode, string key, int position)
    {
        var language = database.TranslationProjectLanguages.Find(project.Id, languageCode);
        if (language == null)
        {
            language = new TranslationProjectLanguage { Project = project, LanguageCode = languageCode };
            database.TranslationProjectLanguages.Add(language);
        }

        var unit = database.TranslationUnits.Find(project.Id, key);
        if (unit == null)
        {
            unit = new TranslationUnit { Project = project, Key = key, Position = position };
            database.TranslationUnits.Add(unit);
        }

        var value = database.TranslationValues.Find(project.Id, key, languageCode);
        if (value == null)
        {
            value = new TranslationValue { Unit = unit, Language = language, Key = key, LanguageCode = languageCode };
            database.TranslationValues.Add(value);
        }

        var suggestion = new TranslationSuggestion { Value = value, Key = key, LanguageCode = languageCode };
        database.TranslationSuggestions.Add(suggestion);
        return suggestion;
    }
}
