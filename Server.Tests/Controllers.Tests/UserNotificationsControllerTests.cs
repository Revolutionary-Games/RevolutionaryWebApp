namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using Xunit;

public class UserNotificationsControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadingCombinedNoticeUpdatesChildrenAndDoesNotReadAnotherUsersNotices(bool all)
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new User("test@example.com", "Test");
        database.Users.Add(user);
        await database.SaveChangesAsync();
        for (var index = 0; index < 3; ++index)
        {
            database.UserNotifications.Add(new UserNotification
            {
                UserId = user.Id, Type = UserNotificationType.TranslationUpdated,
                DeduplicationKey = $"event:{index}", TargetPath = "/translations/test/fi/edit?key=key",
            });
        }

        var other = new UserNotification { UserId = user.Id + 1, DeduplicationKey = "other" };
        database.UserNotifications.Add(other);
        await database.SaveChangesAsync();
        await UserNotificationBatching.Combine(database, new ConfigurationBuilder().Build(), CancellationToken.None);
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = user;
        var controller = new UserNotificationsController(database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        Assert.Equal(1, (await controller.CountUnread()).Value!.Count);
        var notice = Assert.Single((await controller.List()).Value!);
        Assert.Equal(3, notice.CombinedCount);
        if (all)
        {
            Assert.IsType<NoContentResult>(await controller.MarkAllRead());
        }
        else
        {
            Assert.IsType<NoContentResult>(await controller.MarkRead(notice.Id));
        }

        Assert.Equal(0, (await controller.CountUnread()).Value!.Count);
        Assert.Null(other.ReadAt);
        Assert.All(await database.UserNotifications.Where(item => item.UserId == user.Id).ToListAsync(), item =>
            Assert.NotNull(item.ReadAt));
    }
}
