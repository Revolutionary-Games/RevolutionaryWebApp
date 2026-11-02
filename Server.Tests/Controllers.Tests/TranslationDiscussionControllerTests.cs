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

public class TranslationDiscussionControllerTests
{
    [Theory]
    [InlineData(false, "fi", 2)]
    [InlineData(true, "en", 3)]
    public async Task CommentsImmediatelyNotifyOnlySubscribersToTheirLanguage(bool everyone, string language,
        long recipient)
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
        var actor = new User("author@example.com", "Author");
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User));
        database.Users.Add(actor);
        database.TranslationProjects.Add(new TranslationProject { Id = 1, Name = "Test", Slug = "test" });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "key" });
        foreach (var code in new[] { "en", "fi" })
        {
            database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
            {
                ProjectId = 1, LanguageCode = code,
            });
            database.TranslationValues.Add(new TranslationValue { ProjectId = 1, Key = "key", LanguageCode = code });
            database.TranslationSubscriptions.Add(new TranslationSubscription
            {
                ProjectId = 1, LanguageCode = code, UserId = code == "fi" ? 2 : 3,
                EventType = TranslationSubscriptionEventType.Comment,
            });
        }

        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationDiscussionController(NullLogger<TranslationDiscussionController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        var result = await controller.AddComment(new CreateTranslationCommentRequest
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", ForEveryone = everyone, RawMarkdown = "Comment",
        });
        Assert.IsType<CreatedAtActionResult>(result.Result);
        var notice = Assert.Single(await database.UserNotifications.ToListAsync());
        Assert.Equal(recipient, notice.UserId);
        Assert.Equal(NotificationPriority.Low, notice.Priority);
        Assert.Equal(UserNotificationType.TranslationComments, notice.Type);
        Assert.Equal($"/translations/test/{language}/edit?key=key", notice.TargetPath);
    }
}
