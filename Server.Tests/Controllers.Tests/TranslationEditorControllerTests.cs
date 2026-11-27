namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
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
using Shared.Models;
using Xunit;

public class TranslationEditorControllerTests
{
    [Fact]
    public async Task ListsAndNeighborsFollowFileOrderWithAndWithoutStateFiltering()
    {
        await using var database = CreateDatabase();
        database.TranslationProjects.Add(new TranslationProject { Id = 1, Slug = "test", Name = "Test" });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" });
        var keys = new[] { "Z", "A", "M", "removed" };
        for (var index = 0; index < keys.Length; ++index)
        {
            database.TranslationUnits.Add(new TranslationUnit
            {
                ProjectId = 1, Key = keys[index], Position = index, Active = index < 3,
            });
            database.TranslationValues.Add(new TranslationValue
            {
                ProjectId = 1, Key = keys[index], LanguageCode = "fi",
                State = index == 1 ? TranslationState.Translated : TranslationState.NeedsTranslation,
            });
        }

        await database.SaveChangesAsync();
        var controller = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var list = await controller.List(1, "fi");
        Assert.Equal(keys.Take(3), list.Value!.Select(item => item.Key));
        var next = await controller.Neighbor(1, "fi", "Z", 1);
        Assert.Equal("A", Assert.IsType<JsonResult>(next.Result).Value);
        var previous = await controller.Neighbor(1, "fi", "M", -1);
        Assert.Equal("A", Assert.IsType<JsonResult>(previous.Result).Value);
        var inactive = await controller.Get(1, "removed", "fi", CancellationToken.None);
        Assert.False(inactive.Value!.Active);
        Assert.Equal(3, inactive.Value.Position);
        Assert.Null(inactive.Value.TextIndex);
        Assert.Equal(3, inactive.Value.TotalTexts);
        for (var index = 0; index < 3; ++index)
        {
            var item = await controller.Get(1, keys[index], "fi", CancellationToken.None);
            Assert.Equal(index + 1, item.Value!.TextIndex);
            Assert.Equal(3, item.Value.TotalTexts);
        }

        const TranslationState filter = TranslationState.NeedsTranslation;
        var filteredList = await controller.List(1, "fi", state: filter.ToString());
        Assert.Equal(new[] { "Z", "M" }, filteredList.Value!.Select(item => item.Key));
        var filteredNext = await controller.Neighbor(1, "fi", "Z", 1, filter);
        Assert.Equal("M", Assert.IsType<JsonResult>(filteredNext.Result).Value);
        var filteredPrevious = await controller.Neighbor(1, "fi", "A", -1, filter);
        Assert.Equal("Z", Assert.IsType<JsonResult>(filteredPrevious.Result).Value);
        var last = await controller.Neighbor(1, "fi", "M", 1, filter);
        Assert.IsType<NoContentResult>(last.Result);

        var matching = await controller.Get(1, "M", "fi", CancellationToken.None, filter);
        Assert.Equal(2, matching.Value!.TextIndex);
        Assert.Equal(2, matching.Value.TotalTexts);
        var outsideFilter = await controller.Get(1, "A", "fi", CancellationToken.None, filter);
        Assert.Null(outsideFilter.Value!.TextIndex);
        Assert.Equal(2, outsideFilter.Value.TotalTexts);
    }

    [Fact]
    public async Task WarningFilterSkipsIgnoredWarningsAndRemovedStrings()
    {
        await using var database = CreateDatabase();
        await database.TranslationProjects.AddAsync(new TranslationProject { Id = 1, Slug = "test", Name = "Test" });
        await database.TranslationProjectLanguages.AddAsync(new TranslationProjectLanguage
        {
            ProjectId = 1, LanguageCode = "fi",
        });
        var keys = new[] { "first", "ignored", "removed", "last" };
        for (var index = 0; index < keys.Length; ++index)
        {
            var key = keys[index];
            await database.TranslationUnits.AddAsync(new TranslationUnit
            {
                ProjectId = 1, Key = key, Position = index, Active = key != "removed",
            });
            await database.TranslationValues.AddAsync(new TranslationValue
            {
                ProjectId = 1, Key = key, LanguageCode = "fi", State = TranslationState.Verified,
            });
            await database.TranslationProblems.AddAsync(new TranslationProblem
            {
                ProjectId = 1, Key = key, LanguageCode = "fi", Ignored = key == "ignored",
            });
        }

        await database.SaveChangesAsync();
        var controller = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var list = await controller.List(1, "fi", problemsOnly: true);
        Assert.Equal(new[] { "first", "last" }, list.Value!.Select(item => item.Key));

        var next = await controller.Neighbor(1, "fi", "first", 1, problemsOnly: true);
        Assert.Equal("last", Assert.IsType<JsonResult>(next.Result).Value);
        var previous = await controller.Neighbor(1, "fi", "last", -1, problemsOnly: true);
        Assert.Equal("first", Assert.IsType<JsonResult>(previous.Result).Value);

        var matching = await controller.Get(1, "last", "fi", CancellationToken.None, problemsOnly: true);
        Assert.Equal(2, matching.Value!.TextIndex);
        Assert.Equal(2, matching.Value.TotalTexts);
        var ignored = await controller.Get(1, "ignored", "fi", CancellationToken.None, problemsOnly: true);
        Assert.Null(ignored.Value!.TextIndex);
        Assert.Equal(2, ignored.Value.TotalTexts);
    }

    [Fact]
    public async Task SuggestionFilterListsAndNavigatesActiveStringsWithPendingSuggestionsInFileOrder()
    {
        await using var database = CreateDatabase();
        await database.TranslationProjects.AddAsync(new TranslationProject { Id = 1, Slug = "test", Name = "Test" });
        await database.TranslationProjectLanguages.AddRangeAsync(
            new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" },
            new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "sv" });
        var strings = new[]
        {
            (Key: "other-language", Language: "sv", Status: TranslationSuggestionStatus.Open),
            (Key: "rejected", Language: "fi", Status: TranslationSuggestionStatus.Rejected),
            (Key: "removed", Language: "fi", Status: TranslationSuggestionStatus.Open),
            (Key: "accepted", Language: "fi", Status: TranslationSuggestionStatus.Accepted),
            (Key: "Z-open", Language: "fi", Status: TranslationSuggestionStatus.Open),
            (Key: "A-open", Language: "fi", Status: TranslationSuggestionStatus.Open),
        };
        for (var index = 0; index < strings.Length; ++index)
        {
            var (key, language, status) = strings[index];
            await database.TranslationUnits.AddAsync(new TranslationUnit
            {
                ProjectId = 1, Key = key, Position = index, Active = key != "removed",
            });
            await database.TranslationValues.AddAsync(new TranslationValue
            {
                ProjectId = 1, Key = key, LanguageCode = "fi",
            });
            if (language != "fi")
            {
                await database.TranslationValues.AddAsync(new TranslationValue
                {
                    ProjectId = 1, Key = key, LanguageCode = language,
                });
            }

            await database.TranslationSuggestions.AddAsync(new TranslationSuggestion
            {
                ProjectId = 1, Key = key, LanguageCode = language, Status = status,
            });
        }

        await database.SaveChangesAsync();
        var controller = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var list = await controller.List(1, "fi", pendingSuggestionsOnly: true);
        Assert.Equal(new[] { "Z-open", "A-open" }, list.Value!.Select(item => item.Key));

        var next = await controller.Neighbor(1, "fi", "rejected", 1, pendingSuggestionsOnly: true);
        Assert.Equal("Z-open", Assert.IsType<JsonResult>(next.Result).Value);
        var previous = await controller.Neighbor(1, "fi", "Z-open", -1, pendingSuggestionsOnly: true);
        Assert.IsType<NoContentResult>(previous.Result);

        var matching = await controller.Get(1, "A-open", "fi", CancellationToken.None, pendingSuggestionsOnly: true);
        Assert.Equal(2, matching.Value!.TextIndex);
        Assert.Equal(2, matching.Value.TotalTexts);
        var approved = await controller.Get(1, "accepted", "fi", CancellationToken.None, pendingSuggestionsOnly: true);
        Assert.Null(approved.Value!.TextIndex);
        Assert.Equal(2, approved.Value.TotalTexts);
    }

    [Fact]
    public async Task TextIndexHandlesPositionGapsTiesAndMissingLanguageValues()
    {
        await using var database = CreateDatabase();
        database.TranslationProjects.Add(new TranslationProject { Id = 1, Slug = "test", Name = "Test" });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" });
        foreach (var key in new[] { "A", "B", "C", "missing" })
        {
            database.TranslationUnits.Add(new TranslationUnit
            {
                ProjectId = 1, Key = key, Position = key == "C" ? 20 : 10,
            });
            if (key != "missing")
            {
                database.TranslationValues.Add(new TranslationValue
                {
                    ProjectId = 1, Key = key, LanguageCode = "fi",
                });
            }
        }

        await database.SaveChangesAsync();
        var controller = new TranslationEditorController(NullLogger<TranslationEditorController>.Instance, database)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var keys = new[] { "A", "B", "C" };
        for (var index = 0; index < keys.Length; ++index)
        {
            var item = await controller.Get(1, keys[index], "fi", CancellationToken.None);
            Assert.Equal(index + 1, item.Value!.TextIndex);
            Assert.Equal(3, item.Value.TotalTexts);
        }
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }
}
