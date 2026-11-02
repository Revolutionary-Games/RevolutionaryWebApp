namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Hangfire;
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
using Xunit;

public class TranslationAnnouncementsTests
{
    [Fact]
    public async Task PublishingNotifiesSubscribersAndCreditedTranslatorsOnceAndReplacesNotice()
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
        var actor = new User("admin@example.com", "Admin") { Id = 1 };
        database.Users.Add(actor);
        var project = new TranslationProject { Id = 1, Name = "Test", Slug = "test" };
        database.TranslationProjects.Add(project);
        database.TranslationProjectLanguages.Add(new TranslationProjectLanguage { ProjectId = 1, LanguageCode = "fi" });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "key" });
        database.TranslationValues.Add(new TranslationValue
        {
            ProjectId = 1, Key = "key", LanguageCode = "fi", LastEditedByUserId = 2,
        });
        database.TranslationUnits.Add(new TranslationUnit { ProjectId = 1, Key = "credited-only" });
        database.TranslationValues.Add(new TranslationValue
        {
            ProjectId = 1, Key = "credited-only", LanguageCode = "fi", LastEditedByUserId = 5,
        });
        foreach (var userId in new long[] { 2, 3 })
        {
            database.TranslationSubscriptions.Add(new TranslationSubscription
            {
                ProjectId = 1, LanguageCode = "fi", UserId = userId,
                EventType = TranslationSubscriptionEventType.Announcement,
            });
        }

        database.TranslationSubscriptions.Add(new TranslationSubscription
        {
            ProjectId = 1, LanguageCode = "fi", UserId = 4,
            EventType = TranslationSubscriptionEventType.Comment,
        });
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        var controller = new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance,
            database, Substitute.For<IBackgroundJobClient>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        var request = new TranslationAnnouncementDTO
        {
            Message = "**Release deadline**", EndsAt = DateTime.UtcNow.AddDays(1), Severity = SiteNoticeType.Warning,
        };
        Assert.IsType<NoContentResult>(await controller.PublishAnnouncement(1, request));
        var notifications = await database.UserNotifications.ToListAsync();
        Assert.Equal(new long[] { 2, 3, 5 }, notifications.Select(item => item.UserId).OrderBy(id => id).ToArray());
        Assert.All(notifications, item => Assert.Equal(UserNotificationType.TranslationAnnouncement, item.Type));
        Assert.Equal(request.Message, project.GetDTO(false).Announcement!.Message);
        request.Message = "Replacement";
        Assert.IsType<NoContentResult>(await controller.PublishAnnouncement(1, request));
        Assert.Equal("Replacement", project.GetDTO(false).Announcement!.Message);
        Assert.Equal(6, await database.UserNotifications.CountAsync());
        Assert.IsType<NoContentResult>(await controller.DeleteAnnouncement(1));
        Assert.Null(project.GetDTO(false).Announcement);
        Assert.Equal(6, await database.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task MissingOrExpiredEndDateIsRejectedAndExpiredNoticesAreHidden()
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
        var controller = new TranslationProjectsController(NullLogger<TranslationProjectsController>.Instance,
            database, Substitute.For<IBackgroundJobClient>());
        var request = new TranslationAnnouncementDTO { Message = "Notice" };
        Assert.IsType<BadRequestObjectResult>(await controller.PublishAnnouncement(1, request));
        request.EndsAt = DateTime.UtcNow.AddDays(-1);
        Assert.IsType<BadRequestObjectResult>(await controller.PublishAnnouncement(1, request));
        var project = new TranslationProject { AnnouncementMessage = "Expired", AnnouncementEndsAt = request.EndsAt };
        Assert.Null(project.GetDTO(false).Announcement);
        Assert.Empty(database.UserNotifications);
    }
}
