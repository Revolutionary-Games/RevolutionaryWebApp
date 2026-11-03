namespace RevolutionaryWebApp.Server.Tests.Controllers.Tests;

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Controllers;
using Server.Models;
using Server.Services;
using Server.Utilities;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class APIKeysControllerTests
{
    [Fact]
    public async Task ManagementRequiresOwnerOrAdminAndBrowserSession()
    {
        await using var database = Database();
        var owner = new User("owner@example.com", "owner");
        var other = new User("other@example.com", "other");
        database.Users.AddRange(owner, other);
        await database.SaveChangesAsync();
        owner.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        other.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        var key = new APIKey { Id = "abcdefghijklmn", UserId = owner.Id, Secret = Guid.NewGuid().ToString() };
        database.APIKeys.Add(key);
        await database.SaveChangesAsync();

        var controller = Controller(database, other);
        Assert.IsType<ForbidResult>((await controller.List(owner.Id)).Result);
        Assert.IsType<ForbidResult>(await controller.Delete(owner.Id, key.Id));
        Assert.IsType<ForbidResult>(await controller.Update(owner.Id, key.Id, new APIKeySettings()));
        Assert.IsType<ForbidResult>((await controller.Create(owner.Id, new APIKeySettings())).Result);

        controller = Controller(database, owner, false);
        Assert.IsType<ForbidResult>((await controller.List(owner.Id)).Result);
        Assert.IsType<ForbidResult>((await controller.Create(owner.Id, new APIKeySettings())).Result);

        controller = Controller(database, owner);
        var list = await controller.List(owner.Id);
        Assert.Single(Assert.IsType<APIKeyDTO[]>(list.Value));
        Assert.Equal(SelectByHashedProperty.HashForDatabaseValue(key.Id), key.HashedId);
        Assert.Same(key, await database.APIKeys.WhereHashed(nameof(APIKey.Id), key.Id).SingleAsync());
        Assert.Empty(await database.APIKeys.WhereHashed(nameof(APIKey.Id), "unknown-key-id").ToListAsync());
        Assert.IsType<BadRequestObjectResult>(await controller.Update(owner.Id, key.Id,
            new APIKeySettings { ReadOnly = false }));
        Assert.IsType<BadRequestObjectResult>((await controller.Create(owner.Id,
            new APIKeySettings { Scopes = [] })).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Create(owner.Id,
            new APIKeySettings { AllowedIPNetwork = "not-a-network" })).Result);
        var admin = new User("admin@example.com", "admin");
        database.Users.Add(admin);
        await database.SaveChangesAsync();
        admin.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.Admin));
        var adminController = Controller(database, admin);
        Assert.NotNull((await adminController.List(owner.Id)).Value);
        Assert.IsType<ForbidResult>((await adminController.Create(owner.Id, new APIKeySettings())).Result);
        var secret = key.Secret;
        Assert.IsType<OkResult>(await adminController.Update(owner.Id, key.Id,
            new APIKeySettings { Scopes = [APIKeyScope.Builds], AllowedIPNetwork = "192.0.2.0/24" }));
        Assert.Equal(secret, key.Secret);
        Assert.Equal("2", key.Scopes);
        Assert.Equal("192.0.2.0/24", key.AllowedIPNetwork);
        Assert.Single(await database.AdminActions.ToListAsync());

        Assert.IsType<OkResult>(await controller.Update(owner.Id, key.Id, new APIKeySettings
        {
            Scopes = [APIKeyScope.Builds, APIKeyScope.All, APIKeyScope.Users],
        }));
        Assert.Equal("0", key.Scopes);
        var updatedList = await controller.List(owner.Id);
        Assert.Equal(new[] { APIKeyScope.All }, Assert.Single(updatedList.Value!).Scopes);

        Assert.IsType<OkResult>(await controller.Delete(owner.Id, key.Id));
        Assert.Empty(await database.APIKeys.ToListAsync());
    }

    [Fact]
    public async Task KeysCascadeWithOwnerDeletion()
    {
        await using var database = Database();
        var owner = new User("owner@example.com", "owner");
        database.Users.Add(owner);
        await database.SaveChangesAsync();
        database.APIKeys.Add(new APIKey { Id = "abcdefghijklmn", User = owner, Secret = Guid.NewGuid().ToString() });
        await database.SaveChangesAsync();
        database.Users.Remove(owner);
        await database.SaveChangesAsync();
        Assert.Empty(await database.APIKeys.ToListAsync());
    }

    [Theory]
    [InlineData(nameof(APIKey.CreatedAt), SortDirection.Ascending, "third", "first", "second")]
    [InlineData(nameof(APIKey.CreatedAt), SortDirection.Descending, "second", "first", "third")]
    [InlineData(nameof(APIKey.LastUsedAt), SortDirection.Ascending, "second", "third", "first")]
    [InlineData(nameof(APIKey.LastUsedAt), SortDirection.Descending, "first", "third", "second")]
    public async Task ListSortsByRequestedTimestamp(string column, SortDirection direction,
        string first, string second, string third)
    {
        await using var database = Database();
        var owner = new User("sort@example.com", "sort");
        database.Users.Add(owner);
        await database.SaveChangesAsync();
        owner.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        var now = DateTime.UtcNow;
        database.APIKeys.AddRange(
            new APIKey { Id = "first", User = owner, CreatedAt = now, LastUsedAt = now },
            new APIKey { Id = "second", User = owner, CreatedAt = now.AddDays(1), LastUsedAt = now.AddDays(-2) },
            new APIKey { Id = "third", User = owner, CreatedAt = now.AddDays(-1), LastUsedAt = now.AddDays(-1) });
        await database.SaveChangesAsync();

        var controller = Controller(database, owner);
        var result = await controller.List(owner.Id, column, direction);
        Assert.Equal(new[] { first, second, third }, result.Value!.Select(key => key.Id));
        Assert.IsType<BadRequestObjectResult>((await controller.List(owner.Id, nameof(APIKey.Secret))).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.List(owner.Id, "UnknownColumn")).Result);
    }

    private static NotificationsEnabledDb Database() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        Substitute.For<IModelUpdateNotificationSender>());

    private static APIKeysController Controller(NotificationsEnabledDb database, User user, bool session = true)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.Items[AppInfo.CurrentUserMiddlewareKey] = user;
        if (session)
            context.Items[AppInfo.CurrentUserSessionMiddleWareKey] = new Session { User = user };
        return new APIKeysController(database, NullLogger<APIKeysController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }
}
