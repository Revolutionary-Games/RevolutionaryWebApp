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

public class TranslationSuggestionsControllerTests
{
    [Fact]
    public async Task CreatingSuggestionNotifiesOnlyOtherSubscribersForItsLanguageAndProject()
    {
        await using var database = CreateDatabase();
        var actor = new User("author@example.com", "Author");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        database.Users.Add(actor);
        database.TranslationProjects.Add(new TranslationProject { Id = 1, Name = "Test", Slug = "test" });
        database.TranslationProjects.Add(new TranslationProject { Id = 2 });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "sv" });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 2, LanguageCode = "fi" });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "a key", Active = true });
        database.TranslationValues.Add(new TranslationValue { ProjectId = 1, Key = "a key", LanguageCode = "fi" });
        await database.SaveChangesAsync();
        database.TranslationSubscriptions.AddRange(
            new TranslationSubscription
            {
                ProjectId = 1, LanguageCode = "fi", UserId = actor.Id,
                EventType = TranslationSubscriptionEventType.Suggestion,
            },
            new TranslationSubscription
            {
                ProjectId = 1, LanguageCode = "fi", UserId = 100,
                EventType = TranslationSubscriptionEventType.Suggestion,
            },
            new TranslationSubscription
            {
                ProjectId = 1, LanguageCode = "sv", UserId = 101,
                EventType = TranslationSubscriptionEventType.Suggestion,
            },
            new TranslationSubscription
            {
                ProjectId = 2, LanguageCode = "fi", UserId = 102,
                EventType = TranslationSubscriptionEventType.Suggestion,
            },
            new TranslationSubscription
            {
                ProjectId = 1, LanguageCode = "fi", UserId = 103,
                EventType = TranslationSubscriptionEventType.Comment,
            });
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationSuggestionsController(NullLogger<TranslationSuggestionsController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        var result = await controller.Create(new CreateTranslationSuggestionRequest
        {
            ProjectId = 1, Key = "a key", LanguageCode = "fi", Text = "Suggested translation",
        });
        Assert.IsType<CreatedAtActionResult>(result.Result);
        var suggestion = await database.TranslationSuggestions.SingleAsync();
        var notice = Assert.Single(await database.UserNotifications.ToListAsync());
        Assert.Equal(100, notice.UserId);
        Assert.Equal(UserNotificationType.TranslationSuggestionCreated, notice.Type);
        Assert.Equal($"translation-suggestion:{suggestion.Id}:subscriber:100", notice.DeduplicationKey);
        Assert.Equal("/translations/test/fi/edit?key=a%20key", notice.TargetPath);
    }

    [Fact]
    public async Task AcceptingSourceSuggestionInvalidatesTargetsAndNotifiesSubscribers()
    {
        await using var database = CreateDatabase();
        var actor = new User("source@example.com", "Source translator");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User, GroupType.SourceTextTranslator));
        database.Users.Add(actor);
        database.TranslationProjects.Add(new TranslationProject { Id = 1, Name = "Test", Slug = "test" });
        foreach (var code in new[] { "en", "fi" })
        {
            database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
            {
                ProjectId = 1, LanguageCode = code,
            });
        }

        var unit = new TranslationUnit { ProjectId = 1, Key = "key", SourceText = "Old", SourceRevision = 1 };
        database.TranslationUnits.Add(unit);
        database.TranslationValues.Add(new TranslationValue
        {
            ProjectId = 1, Key = "key", LanguageCode = "en", Text = "Old", Unit = unit,
        });
        var target = new TranslationValue
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", Text = "Translation", Unit = unit,
            State = TranslationState.Verified,
        };
        database.TranslationValues.Add(target);
        var suggestion = new TranslationSuggestion
        {
            ProjectId = 1, Key = "key", LanguageCode = "en", Text = "New", SourceRevision = 1,
            AuthorUser = actor,
        };
        database.TranslationSuggestions.Add(suggestion);
        database.TranslationSubscriptions.Add(new TranslationSubscription
        {
            ProjectId = 1, LanguageCode = "fi", UserId = 2,
            EventType = TranslationSubscriptionEventType.SourceChange,
        });
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationSuggestionsController(NullLogger<TranslationSuggestionsController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        Assert.IsType<NoContentResult>(await controller.Accept(suggestion.Id));
        Assert.Equal("New", unit.SourceText);
        Assert.Equal(2, unit.SourceRevision);
        Assert.Equal(TranslationState.NeedsTranslation, target.State);
        Assert.Equal(UserNotificationType.TranslationSourceChanged,
            Assert.Single(await database.UserNotifications.ToListAsync()).Type);
    }

    [Fact]
    public async Task VoteChangesRecountScoreAndKeepUserVoteRows()
    {
        await using var database = CreateDatabase();
        var actor = new User("voter@example.com", "Voter");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        database.Users.Add(actor);
        database.TranslationProjects.Add(new TranslationProject { Id = 1 });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "key" });
        database.TranslationValues.Add(new TranslationValue { ProjectId = 1, Key = "key", LanguageCode = "fi" });
        var suggestion = new TranslationSuggestion
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", AuthorUserId = 100, Score = 10,
        };
        database.TranslationSuggestions.Add(suggestion);
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationSuggestionsController(NullLogger<TranslationSuggestionsController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        foreach (var vote in new[] { 1, 1, -1, 0, 0 })
        {
            Assert.IsType<NoContentResult>(await controller.Vote(suggestion.Id,
                new VoteOnTranslationSuggestionRequest { Value = vote }));
            Assert.Equal(vote, suggestion.Score);
            Assert.Equal(vote, suggestion.GetDTO().Score);
            Assert.Equal(vote == 0 ? 0 : 1, await database.TranslationSuggestionVotes.CountAsync());
        }
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }
}
