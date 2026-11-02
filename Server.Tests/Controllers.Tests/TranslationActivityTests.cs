namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Server.Controllers;
using Server.Models;
using Server.Models.Translations;
using Shared.Models;
using Xunit;

public class TranslationActivityTests
{
    [Fact]
    public async Task ActivityIsPagedAndLimitedToTheProjectAndSelectedLanguageOrSharedSource()
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        database.Users.Add(new User("translator@example.com", "Translator") { Id = 42 });
        database.TranslationProjects.Add(new TranslationProject { Id = 1, Name = "Test", Slug = "test" });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" });
        foreach (var code in new[] { "en", "sv" })
        {
            database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
            {
                ProjectId = 1, LanguageCode = code,
            });
        }

        for (var index = 0; index < 60; ++index)
        {
            database.TranslationChanges.Add(new TranslationChange
            {
                ProjectId = 1, Key = $"key{index}", LanguageCode = "fi", NewText = $"Text {index}",
                CreatedAt = DateTime.UtcNow.AddMinutes(-index), ActorUserId = index == 0 ? 42 : null,
            });
        }

        database.TranslationChanges.Add(new TranslationChange { ProjectId = 1, Key = "source", LanguageCode = "en" });
        database.TranslationChanges.Add(new TranslationChange { ProjectId = 1, Key = "other", LanguageCode = "sv" });
        database.TranslationChanges.Add(new TranslationChange { ProjectId = 2, Key = "private", LanguageCode = "fi" });
        foreach (var language in new[] { "fi", "en", "sv" })
        {
            database.TranslationComments.Add(new TranslationComment
            {
                ProjectId = 1, Key = "key", LanguageCode = language, RawMarkdown = "Comment",
            });
        }

        database.TranslationComments.Add(new TranslationComment
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", RawMarkdown = "Deleted comment", Deleted = true,
        });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "active" });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "removed", Active = false });
        foreach (var key in database.TranslationChanges.Local.Select(item => item.Key)
                     .Concat(database.TranslationComments.Local.Select(item => item.Key)).Distinct().ToArray())
        {
            database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = key, Active = false });
            foreach (var code in new[] { "fi", "en", "sv" })
                database.TranslationValues.Add(new TranslationValue { ProjectId = 1, Key = key, LanguageCode = code });
        }

        await database.SaveChangesAsync();
        var controller = new TranslationsController(database);
        var first = (await controller.Recent("test", "fi")).Value!;
        var second = (await controller.Recent("test", "fi", page: 2)).Value!;
        Assert.Equal(50, first.Results.Length);
        Assert.Equal(14, second.Results.Length);
        var allActivity = first.Results.Concat(second.Results).ToArray();
        Assert.Equal(allActivity.OrderByDescending(item => item.CreatedAt).Select(item => item.CreatedAt),
            allActivity.Select(item => item.CreatedAt));
        Assert.Equal(61, allActivity.Count(item => item.Description == "Translation edit"));
        Assert.Equal(2, allActivity.Count(item => item.Description == "Comment"));
        Assert.Equal("active", Assert.Single(allActivity, item => item.Description == "New key").Key);
        Assert.All(allActivity, item =>
            Assert.Contains(item.LanguageCode, new[] { "fi", "en" }));
        var projectFirst = (await controller.Recent("test")).Value!;
        var projectSecond = (await controller.Recent("test", page: 2)).Value!;
        Assert.Equal(66, projectFirst.Results.Length + projectSecond.Results.Length);
        Assert.Contains(projectFirst.Results.Concat(projectSecond.Results), item => item.LanguageCode == "sv");
        var mixed = (await controller.Recent("test", types: "Comments,Source")).Value!;
        Assert.Equal(4, mixed.Results.Length);
        Assert.All(mixed.Results, item => Assert.True(item.Description == "Comment" || item.Key == "source"));
        Assert.Empty((await controller.Recent("test", types: "None")).Value!.Results);
        Assert.Empty((await controller.Recent("test", username: "missing-user")).Value!.Results);
        var userActivity = (await controller.Recent("test", username: "translator")).Value!;
        Assert.Equal(42, Assert.Single(userActivity.Results).UserId);
        Assert.IsType<BadRequestObjectResult>((await controller.Recent("test", types: "invalid")).Result);
        var source = (await controller.Recent("test", "fi", TranslationActivityView.Source)).Value!;
        var comments = (await controller.Recent("test", "fi", TranslationActivityView.Comments)).Value!;
        var keys = (await controller.Recent("test", "fi", TranslationActivityView.Keys)).Value!;
        Assert.Equal("source", Assert.Single(source.Results).Key);
        Assert.Equal(2, comments.Results.Length);
        Assert.Equal("active", Assert.Single(keys.Results).Key);
        Assert.IsType<BadRequestObjectResult>((await controller.Recent("test", "fi", page: 0)).Result);
        var invalidView = await controller.Recent("test", "fi", (TranslationActivityView)999);
        Assert.IsType<BadRequestObjectResult>(invalidView.Result);
        Assert.IsType<NotFoundResult>((await controller.Recent("test", "missing")).Result);
    }
}
