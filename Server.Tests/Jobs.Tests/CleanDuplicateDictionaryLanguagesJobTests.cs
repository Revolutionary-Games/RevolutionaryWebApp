namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Jobs.Maintenance;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Xunit;

public class CleanDuplicateDictionaryLanguagesJobTests
{
    [Theory]
    [InlineData("zh_CN", "zh-Hans", "translation", null)]
    [InlineData("zh-Hant", "zh_TW", null, "explanation")]
    [InlineData("pt_BR", "pt-BR", "translation", "explanation")]
    [InlineData("pt_BR", "pt-BR", "", "")]
    public async Task KeepsOnlyPopulatedRowOrPreferredEmptyRow(string firstCode, string secondCode,
        string? text, string? explanation)
    {
        await using var database = CreateDatabase();
        var operation = await Seed(database);
        var populated = new DictionaryTermTranslation
        {
            DictionaryId = 1, Word = "Cat", LanguageCode = firstCode,
            Text = text ?? string.Empty, Explanation = explanation,
        };
        database.DictionaryTermTranslations.AddRange(populated, new DictionaryTermTranslation
        {
            DictionaryId = 1, Word = "Cat", LanguageCode = secondCode,
        });
        await database.SaveChangesAsync();
        var job = CreateJob(database);

        await job.Execute(operation.Id, CancellationToken.None);

        var remaining = Assert.Single(await database.DictionaryTermTranslations.ToListAsync());
        Assert.Equal(text ?? string.Empty, remaining.Text);
        Assert.Equal(explanation ?? string.Empty, remaining.Explanation ?? string.Empty);
        Assert.Equal(string.IsNullOrEmpty(text) && string.IsNullOrEmpty(explanation) ? "pt-BR" : firstCode,
            remaining.LanguageCode);
        if (!string.IsNullOrEmpty(text) || !string.IsNullOrEmpty(explanation))
            Assert.Equal(populated.Version, remaining.Version);
        Assert.False(operation.Failed);
        Assert.NotNull(operation.FinishedAt);
        Assert.Contains("Deleted 1", operation.ExtendedDescription);

        await job.Execute(operation.Id, CancellationToken.None);
        Assert.Single(await database.DictionaryTermTranslations.ToListAsync());
        Assert.Contains("Deleted 0", operation.ExtendedDescription);
    }

    [Theory]
    [InlineData("same", null, "same", null)]
    [InlineData("translation", null, "", "explanation")]
    [InlineData("", "first", "", "second")]
    public async Task ConflictsFailWithoutDeletingAnyRows(string firstText, string? firstExplanation,
        string secondText, string? secondExplanation)
    {
        await using var database = CreateDatabase();
        var operation = await Seed(database);
        database.DictionaryTerms.Add(new DictionaryTerm { DictionaryId = 1, Word = "Dog" });
        database.DictionaryTermTranslations.AddRange(
            new DictionaryTermTranslation { DictionaryId = 1, Word = "Dog", LanguageCode = "pt_BR" },
            new DictionaryTermTranslation { DictionaryId = 1, Word = "Dog", LanguageCode = "pt-BR" },
            new DictionaryTermTranslation
            {
                DictionaryId = 1, Word = "Cat", LanguageCode = "zh_CN", Text = firstText,
                Explanation = firstExplanation,
            },
            new DictionaryTermTranslation
            {
                DictionaryId = 1, Word = "Cat", LanguageCode = "zh-Hans", Text = secondText,
                Explanation = secondExplanation,
            });
        await database.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateJob(database).Execute(operation.Id, CancellationToken.None));

        Assert.Contains("Cat", exception.Message);
        Assert.Contains("zh-Hans", exception.Message);
        Assert.Equal(4, await database.DictionaryTermTranslations.CountAsync());
        Assert.False(database.ChangeTracker.HasChanges());
        Assert.True(operation.Failed);
        Assert.Contains("No entries were deleted", operation.ExtendedDescription);
    }

    [Fact]
    public async Task LanguageSpecificTranslationsRequireManualResolution()
    {
        await using var database = CreateDatabase();
        var operation = await Seed(database);
        database.DictionaryLanguageTerms.AddRange(
            new DictionaryLanguageTerm
            {
                DictionaryId = 1, LanguageCode = "pt_BR", Word = "Gato", SourceWord = "Cat",
            },
            new DictionaryLanguageTerm
            {
                DictionaryId = 1, LanguageCode = "pt-BR", Word = "Gato", SourceWord = "Cat",
            });
        await database.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateJob(database).Execute(operation.Id, CancellationToken.None));

        Assert.Equal(2, await database.DictionaryLanguageTerms.CountAsync());
        Assert.True(operation.Failed);
    }

    [Fact]
    public async Task DifferentDictionariesContextsAndLanguagesAreNotDuplicates()
    {
        await using var database = CreateDatabase();
        var operation = await Seed(database);
        database.TranslationDictionaries.Add(new TranslationDictionary { Id = 2, Name = "Other" });
        database.DictionaryTerms.AddRange(
            new DictionaryTerm { DictionaryId = 2, Word = "Cat" },
            new DictionaryTerm { DictionaryId = 1, Word = "Cat", Context = "animal" });
        database.DictionaryTermTranslations.AddRange(
            new DictionaryTermTranslation
            {
                DictionaryId = 1, Word = "Cat", LanguageCode = "zh_CN", Text = "Simplified",
            },
            new DictionaryTermTranslation
            {
                DictionaryId = 1, Word = "Cat", LanguageCode = "zh_TW", Text = "Traditional",
            },
            new DictionaryTermTranslation
            {
                DictionaryId = 1, Word = "Cat", Context = "animal", LanguageCode = "zh-Hans", Text = "Context",
            },
            new DictionaryTermTranslation
            {
                DictionaryId = 2, Word = "Cat", LanguageCode = "zh-Hans", Text = "Other dictionary",
            });
        await database.SaveChangesAsync();

        await CreateJob(database).Execute(operation.Id, CancellationToken.None);

        Assert.False(operation.Failed);
        Assert.Equal(4, await database.DictionaryTermTranslations.CountAsync());
        Assert.Contains("Deleted 0", operation.ExtendedDescription);
    }

    [Fact]
    public void OperationIsAvailableToMaintenanceController()
    {
        using var database = CreateDatabase();
        var controller = new MaintenanceController(NullLogger<MaintenanceController>.Instance, database,
            Substitute.For<Hangfire.IBackgroundJobClient>());
        Assert.Contains(controller.GetAvailableMaintenanceOperations(),
            operation => operation.Name == "cleanDuplicateDictionaryLanguages");
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }

    private static CleanDuplicateDictionaryLanguagesJob CreateJob(NotificationsEnabledDb database)
    {
        return new CleanDuplicateDictionaryLanguagesJob(
            NullLogger<CleanDuplicateDictionaryLanguagesJob>.Instance, database, database);
    }

    private static async Task<ExecutedMaintenanceOperation> Seed(NotificationsEnabledDb database)
    {
        database.TranslationDictionaries.Add(new TranslationDictionary { Id = 1, Name = "Test" });
        database.DictionaryTerms.Add(new DictionaryTerm { DictionaryId = 1, Word = "Cat" });
        var operation = new ExecutedMaintenanceOperation("cleanDuplicateDictionaryLanguages");
        database.ExecutedMaintenanceOperations.Add(operation);
        await database.SaveChangesAsync();
        return operation;
    }
}
