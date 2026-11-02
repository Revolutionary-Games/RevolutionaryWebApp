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
    [InlineData(GroupType.User, true, 14, true)]
    [InlineData(GroupType.User, true, 16, false)]
    [InlineData(GroupType.User, false, 1, false)]
    [InlineData(GroupType.Admin, false, 60, true)]
    [InlineData(GroupType.ContentModerator, false, 60, true)]
    [InlineData(GroupType.Developer, false, 1, false)]
    public async Task DeleteEnforcesOwnershipTimeAndModeration(GroupType group, bool own, int ageMinutes,
        bool allowed)
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
        var actor = new User("actor@example.com", "Actor") { Id = 1 };
        actor.SetGroupsFromLauncherLinkCache(new CachedUserGroups(GroupType.User, group));
        database.Users.Add(actor);
        database.TranslationProjects.Add(new TranslationProject { Id = 1, Name = "Test", Slug = "test" });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "key" });
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage
        {
            ProjectId = 1, LanguageCode = "fi",
        });
        database.TranslationValues.Add(new TranslationValue { ProjectId = 1, Key = "key", LanguageCode = "fi" });
        var comment = new TranslationComment
        {
            Id = 1, ProjectId = 1, Key = "key", LanguageCode = "fi", AuthorUserId = own ? 1 : 2,
            RawMarkdown = "Comment", CreatedAt = DateTime.UtcNow.AddMinutes(-ageMinutes),
        };
        database.TranslationComments.Add(comment);
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationDiscussionController(NullLogger<TranslationDiscussionController>.Instance,
            database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await controller.DeleteComment(comment.Id);

        Assert.Equal(allowed, comment.Deleted);
        if (allowed)
        {
            Assert.IsType<OkResult>(result);
            Assert.Empty((await controller.Comments(1, "key", "fi")).Value!);
            Assert.IsType<NotFoundResult>(await controller.DeleteComment(comment.Id));
        }
        else
        {
            Assert.IsType<ForbidResult>(result);
        }

        var notices = await database.UserNotifications.ToListAsync();
        if (allowed && !own)
        {
            var notice = Assert.Single(notices);
            Assert.Equal(2, notice.UserId);
            Assert.Equal(UserNotificationType.TranslationCommentDeleted, notice.Type);
            Assert.Contains("moderator deleted", notice.Message);
        }
        else
        {
            Assert.Empty(notices);
        }
    }

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
