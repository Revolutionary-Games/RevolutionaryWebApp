namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Threading.Tasks;
using Hubs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Server.Authorization;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Shared;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class UserManagementControllerTests
{
    [Theory]
    [InlineData(false, false, " New Name ", "New Name")]
    [InlineData(false, false, "   ", null)]
    [InlineData(true, true, "New Name", "New Name")]
    [InlineData(true, false, "New Name", "Original Name")]
    public async Task NameChangesRequireOwnerOrAdminAndPreserveUsername(bool otherUser, bool admin,
        string name, string? expectedName)
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IModelUpdateNotificationSender>());
        var target = new User("target@example.com", "username") { DisplayName = "Original Name" };
        target.ProcessGroupDataFromLoadedGroups();
        var actor = otherUser ? new User("actor@example.com", "actor") : target;
        if (admin)
            actor.Groups.Add(new UserGroup(GroupType.Admin, "Admin"));
        actor.ProcessGroupDataFromLoadedGroups();
        database.Users.Add(target);
        if (otherUser)
            database.Users.Add(actor);
        await database.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        context.Items[AppInfo.AuthenticationScopeRestrictionMiddleWareKey] = AuthenticationScopeRestriction.None;
        var controller = new UserManagementController(Substitute.For<ILogger<UserManagementController>>(), database,
            Substitute.For<IHubContext<NotificationsHub, INotifications>>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var result = await controller.UpdateName(target.Id, new UserNameUpdateRequest { DisplayName = name });

        if (otherUser && !admin)
        {
            Assert.IsType<NotFoundResult>(result);
        }
        else
        {
            Assert.IsType<OkResult>(result);
        }

        Assert.Equal(expectedName, target.DisplayName);
        Assert.Equal("username", target.UserName);
        Assert.Equal(expectedName, target.GetDTO(RecordAccessLevel.Private).DisplayName);
        Assert.Equal(otherUser && admin ? 1 : 0, await database.AdminActions.CountAsync());
    }
}
