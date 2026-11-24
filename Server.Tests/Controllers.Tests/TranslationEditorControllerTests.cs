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
