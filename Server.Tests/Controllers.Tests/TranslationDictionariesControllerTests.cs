namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
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

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }
}
