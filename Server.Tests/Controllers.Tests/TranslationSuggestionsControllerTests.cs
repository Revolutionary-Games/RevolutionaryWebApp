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

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }
}
