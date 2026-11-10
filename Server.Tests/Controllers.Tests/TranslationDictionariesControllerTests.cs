namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Models.Translations;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class TranslationDictionariesControllerTests
{
    [Fact]
    public async Task DictionarySourceExplanationEditCreatesDatabaseActionLog()
    {
        await using var database = CreateDatabase();
        var actor = new User("manager@example.com", "Manager");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User, GroupType.DictionaryManager));
        database.Users.Add(actor);
        database.TranslationDictionaries.Add(new TranslationDictionary { Id = 1, Name = "Test" });
        database.DictionaryTerms.Add(new DictionaryTerm { DictionaryId = 1, Word = "term", Context = string.Empty });
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationDictionariesController(NullLogger<TranslationDictionariesController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        Assert.IsType<NoContentResult>(await controller.SaveSourceTerm(new SaveDictionaryTermRequest
        {
            DictionaryId = 1, Word = "term", NewWord = "term", Explanation = "Updated explanation",
        }));
        var entry = Assert.Single(await database.ActionLogEntries.ToListAsync());
        Assert.Equal(actor.Id, entry.PerformedById);
        Assert.Contains("Updated explanation", entry.Extended);
    }

    [Theory]
    [InlineData(" TEST ", 2)]
    [InlineData("tapaus", 1)]
    [InlineData("animal", 1)]
    [InlineData("missing", 0)]
    public async Task SearchFindsSourceWordsLanguageSpecificWordsAndContext(string search, int expectedCount)
    {
        await using var database = CreateDatabase();
        await database.TranslationDictionaries.AddAsync(new TranslationDictionary { Id = 1, Name = "Test" });
        await database.DictionaryTerms.AddAsync(new DictionaryTerm { DictionaryId = 1, Word = "Test" });
        await database.DictionaryTerms.AddAsync(new DictionaryTerm
        {
            DictionaryId = 1, Word = "Cat", Context = "animal",
        });
        await database.DictionaryLanguageTerms.AddAsync(new DictionaryLanguageTerm
        {
            DictionaryId = 1, LanguageCode = "fi", Word = "Testitapaus", SourceWord = "test case",
        });
        await database.DictionaryLanguageTerms.AddAsync(new DictionaryLanguageTerm
        {
            DictionaryId = 1, LanguageCode = "de", Word = "Testfall", SourceWord = "test case",
        });
        await database.SaveChangesAsync();
        var controller = new TranslationDictionariesController(NullLogger<TranslationDictionariesController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.Terms(1, language: "fi", search: search);

        Assert.NotNull(result.Value);
        Assert.Equal(expectedCount, result.Value.Results.Length);
    }

    [Fact]
    public async Task UntranslatableTermsAppearInOtherLanguagesAndRejectTranslationSaves()
    {
        await using var database = CreateDatabase();
        var actor = new User("translator@example.com", "Translator");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        await database.Users.AddAsync(actor);
        await database.TranslationDictionaries.AddAsync(new TranslationDictionary { Id = 1, Name = "Test" });
        var term = new DictionaryTerm
        {
            DictionaryId = 1, Word = "Ladle", Context = "library", Explanation = "Software library",
            Untranslatable = true,
        };
        await database.DictionaryTerms.AddAsync(term);
        await database.DictionaryTermTranslations.AddAsync(new DictionaryTermTranslation
        {
            DictionaryId = 1, Word = "Ladle", Context = "library", LanguageCode = "fi", Text = "Old translation",
        });
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationDictionariesController(NullLogger<TranslationDictionariesController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await controller.Terms(1, language: "fi");
        var dto = Assert.Single(result.Value!.Results);
        Assert.True(dto.Untranslatable);
        Assert.Equal("Ladle", dto.TargetText);
        Assert.Equal("Software library", dto.SourceExplanation);
        Assert.Equal("Ladle", term.GetDTO().TargetText);
        Assert.IsType<BadRequestObjectResult>(await controller.SaveTranslation(new SaveDictionaryTranslationRequest
        {
            DictionaryId = 1, Word = "Ladle", Context = "library", LanguageCode = "fi", Text = "Kauha",
        }));
        Assert.Equal("Old translation", (await database.DictionaryTermTranslations.SingleAsync()).Text);
    }

    [Theory]
    [InlineData("zh_CN", "zh-Hans")]
    [InlineData("zh-Hant", "zh_TW")]
    [InlineData("pt_BR", "pt-BR")]
    [InlineData("pt-BR", "pt_BR")]
    public async Task EquivalentLanguagesShareTranslationsAndEdits(string storedCode, string requestedCode)
    {
        await using var database = CreateDatabase();
        var actor = new User("translator@example.com", "Translator");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        database.Users.Add(actor);
        database.TranslationDictionaries.Add(new TranslationDictionary { Id = 1, Name = "Test" });
        database.TranslationProjects.Add(new TranslationProject { Id = 1, DictionaryId = 1 });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
        {
            ProjectId = 1, LanguageCode = storedCode,
        });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
        {
            ProjectId = 1, LanguageCode = requestedCode,
        });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "cat", SourceText = "Cat" });
        database.DictionaryTerms.Add(new DictionaryTerm { DictionaryId = 1, Word = "Cat" });
        database.DictionaryTermTranslations.Add(new DictionaryTermTranslation
        {
            DictionaryId = 1, Word = "Cat", LanguageCode = storedCode, Text = "Translated cat",
        });
        database.DictionaryLanguageTerms.Add(new DictionaryLanguageTerm
        {
            DictionaryId = 1, LanguageCode = storedCode, Word = "Local cat", SourceWord = "Cat",
        });
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationDictionariesController(NullLogger<TranslationDictionariesController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await controller.Terms(1, language: requestedCode);
        Assert.NotNull(result.Value);
        Assert.Contains(result.Value.Results, term => term.TargetText == "Translated cat");
        Assert.Contains(result.Value.Results, term => term.TargetText == "Local cat");
        Assert.Single((await controller.Languages(1)).Value!);
        var matches = (await controller.Matching(1, "cat", requestedCode, CancellationToken.None)).Value;
        Assert.NotNull(matches);
        Assert.Contains(matches, term => term.TargetText == "Translated cat");
        Assert.Contains(matches, term => term.TargetText == "Local cat");
        Assert.IsType<NoContentResult>(await controller.SaveTranslation(new SaveDictionaryTranslationRequest
        {
            DictionaryId = 1, Word = "Cat", LanguageCode = requestedCode, Text = "Updated cat",
            State = TranslationState.Translated,
        }));
        var translation = Assert.Single(await database.DictionaryTermTranslations.ToListAsync());
        Assert.Equal(storedCode, translation.LanguageCode);
        Assert.Equal("Updated cat", translation.Text);
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }
}
