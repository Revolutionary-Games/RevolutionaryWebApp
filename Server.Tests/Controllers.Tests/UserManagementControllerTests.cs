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
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProfilesExposeOnlyPublicFieldsAndStaffCanInspect(bool loggedIn, bool moderator)
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IModelUpdateNotificationSender>());
        var target = new User("target@example.com", "username") { DisplayName = "Full Name" };
        target.ProcessGroupDataFromLoadedGroups();
        var actor = new User("actor@example.com", "actor");
        if (moderator)
            actor.Groups.Add(new UserGroup(GroupType.ContentModerator, "Moderator"));
        actor.ProcessGroupDataFromLoadedGroups();
        database.Users.AddRange(target, actor);
        await database.SaveChangesAsync();
        var context = new DefaultHttpContext();
        if (loggedIn)
            context.Items[AppInfo.CurrentUserMiddlewareKey] = actor;
        context.Items[AppInfo.AuthenticationScopeRestrictionMiddleWareKey] = AuthenticationScopeRestriction.None;
        var controller = new UserManagementController(Substitute.For<ILogger<UserManagementController>>(), database,
            Substitute.For<IHubContext<NotificationsHub, INotifications>>())
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };

        var profile = (await controller.GetPublicProfile("username")).Value!;
        Assert.Equal(target.UserName, profile.Name);
        Assert.Equal(target.CreatedAt, profile.CreatedAt);
        Assert.Equal(loggedIn ? "Full Name" : null, profile.DisplayName);
        if (loggedIn)
        {
            var list = await controller.GetList("UserName", SortDirection.Ascending, 1, 10, null);
            if (moderator)
            {
                Assert.NotNull(list.Value);
            }
            else
            {
                Assert.IsType<ForbidResult>(list.Result);
            }

            if (!moderator)
                Assert.IsType<NotFoundResult>((await controller.GetUser(target.Id)).Result);
        }
    }

    [Theory]
    [InlineData(false, false, " New Name ", "New Name")]
    [InlineData(false, false, "   ", null)]
    [InlineData(true, true, "New Name", "New Name")]
    [InlineData(true, false, "New Name", "Original Name")]
    [InlineData(false, false, "王小明", "王小明")]
    [InlineData(false, false, "𠀀", "𠀀")]
    [InlineData(false, false, "Jose\u0301 O’Neill-Smith", "Jose\u0301 O’Neill-Smith")]
    [InlineData(false, false, "محمد", "محمد")]
    [InlineData(false, false, "山田・太郎", "山田・太郎")]
    [InlineData(false, false, "Name\n", "Original Name", true)]
    [InlineData(false, false, "\tName", "Original Name", true)]
    [InlineData(false, false, "\r\n", "Original Name", true)]
    [InlineData(false, false, "Name\0", "Original Name", true)]
    [InlineData(false, false, "Name\u2028Other", "Original Name", true)]
    [InlineData(false, false, "Name\u202EOther", "Original Name", true)]
    [InlineData(false, false, "Name\u200BOther", "Original Name", true)]
    [InlineData(false, false, "Name😀", "Original Name", true)]
    [InlineData(false, false, "Name❤", "Original Name", true)]
    [InlineData(false, false, "Name1\uFE0F\u20E3", "Original Name", true)]
    [InlineData(false, false, "Name\U000E0100", "Original Name", true)]
    [InlineData(false, false, "Name\uE000", "Original Name", true)]
    [InlineData(false, false, "Name�", "Original Name", true)]
    public async Task NameChangesRequireOwnerOrAdminAndPreserveUsername(bool otherUser, bool admin,
        string name, string? expectedName, bool rejected = false)
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

        if (rejected)
        {
            Assert.IsType<BadRequestObjectResult>(result);
        }
        else if (otherUser && !admin)
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
