namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using Xunit;

public class TranslationCreditSettingsControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdatingPreferenceLoadsGroupsForUserNotifications(bool useEmail)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var user = new User("test@example.com", "Test") { UseEmailInTranslationCredits = !useEmail };
        user.Groups.Add(new UserGroup(GroupType.Developer, "Developer"));
        await using (var setup = new ApplicationDbContext(options))
        {
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
        }

        var sender = Substitute.For<IModelUpdateNotificationSender>();
        sender.OnChangesDetected(Arg.Any<EntityState>(), Arg.Any<IUpdateNotifications>(), Arg.Any<bool>())
            .Returns(call => call.Arg<IUpdateNotifications>().GetNotifications(call.Arg<EntityState>()));
        await using var database = new NotificationsEnabledDb(options, sender);
        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = user;
        var controller = new TranslationCreditSettingsController(database)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await controller.Update(new TranslationCreditSettingsDTO
        {
            UseEmailInTranslationCredits = useEmail,
        });

        Assert.Equal(useEmail, result.Value!.UseEmailInTranslationCredits);
        var updated = await database.Users.SingleAsync();
        Assert.True(updated.AccessCachedGroupsOrThrow().HasGroup(GroupType.Developer));
        await sender.Received(1).SendNotifications(Arg.Any<IEnumerable<Tuple<SerializedNotification, string>>>());
        await using var verification = new ApplicationDbContext(options);
        Assert.Equal(useEmail, (await verification.Users.SingleAsync()).UseEmailInTranslationCredits);
    }
}
