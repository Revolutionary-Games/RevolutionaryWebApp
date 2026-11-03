namespace RevolutionaryWebApp.Server.Tests.Authorization.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Models;
using Server.Services;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using Xunit;

public class APIKeyNotificationTests
{
    [Fact]
    public async Task SavingKeySendsOwnerNotificationsForCreationChangesAndDeletionWithoutSecrets()
    {
        var sent = new List<Tuple<SerializedNotification, string>>();
        var sender = Substitute.For<IModelUpdateNotificationSender>();
        sender.OnChangesDetected(Arg.Any<EntityState>(), Arg.Any<IUpdateNotifications>(), Arg.Any<bool>())
            .Returns(call => call.Arg<IUpdateNotifications>() is APIKey key ?
                key.GetNotifications(call.Arg<EntityState>()) :
                []);
        sender.SendNotifications(Arg.Any<IEnumerable<Tuple<SerializedNotification, string>>>())
            .Returns(call =>
            {
                sent.AddRange(call.Arg<IEnumerable<Tuple<SerializedNotification, string>>>());
                return Task.CompletedTask;
            });
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, sender);
        var owner = new User("api@example.com", "api");
        database.Users.Add(owner);
        await database.SaveChangesAsync();
        var key = new APIKey { Id = "abcdefghijklmn", User = owner, Secret = Guid.NewGuid().ToString() };
        database.APIKeys.Add(key);
        await database.SaveChangesAsync();
        key.ReadOnly = false;
        key.Scopes = "2";
        key.LastUsedAt = DateTime.UtcNow;
        await database.SaveChangesAsync();
        database.APIKeys.Remove(key);
        await database.SaveChangesAsync();

        Assert.Equal(3, sent.Count);
        Assert.Equal(new[]
        {
            ListItemChangeType.ItemAdded, ListItemChangeType.ItemUpdated, ListItemChangeType.ItemDeleted,
        }, sent.Select(item => ((APIKeyListUpdated)item.Item1).Type));
        Assert.All(sent, item =>
        {
            Assert.Equal(NotificationGroups.UserAPIKeysUpdatedPrefix + owner.Id, item.Item2);
            var notification = Assert.IsType<APIKeyListUpdated>(item.Item1);
            Assert.Equal(owner.Id, notification.UserId);
            Assert.Equal(key.Id, notification.Item.Id);
            var json = JsonSerializer.Serialize(notification);
            Assert.DoesNotContain(key.Secret, json);
            Assert.DoesNotContain(key.HashedId!, json);
        });
        var update = Assert.IsType<APIKeyListUpdated>(sent[1].Item1);
        Assert.Equal(key.LastUsedAt, update.Item.LastUsedAt);
        Assert.False(update.Item.ReadOnly);
        Assert.Equal(new[] { APIKeyScope.Builds }, update.Item.Scopes);
    }

    [Theory]
    [InlineData("owner", true)]
    [InlineData("admin", true)]
    [InlineData("other", false)]
    [InlineData("anonymous", false)]
    public async Task OnlyOwnerAndAdminsCanSubscribeToKeyNotifications(string actor, bool allowed)
    {
        await using var database = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var owner = new User("owner@example.com", "owner");
        var other = new User("other@example.com", "other");
        database.Users.AddRange(owner, other);
        await database.SaveChangesAsync();
        owner.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        other.ForceResolveGroupsForTesting(new CachedUserGroups(actor == "admin" ? GroupType.Admin : GroupType.User));
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("key-notification-test");
        context.Items.Returns(new Dictionary<object, object?>
        {
            ["User"] = actor == "anonymous" ? null : actor == "owner" ? owner : other,
            ["Session"] = null,
        });
        var groups = Substitute.For<IGroupManager>();
        var hub = new NotificationsHub(NullLogger<NotificationsHub>.Instance,
            Substitute.For<ITokenVerifier>(), database)
        {
            Context = context,
            Groups = groups,
        };
        var group = NotificationGroups.UserAPIKeysUpdatedPrefix + owner.Id;
        if (allowed)
        {
            await hub.JoinGroup(group);
            await groups.Received(1).AddToGroupAsync(context.ConnectionId, group);
        }
        else
        {
            await Assert.ThrowsAsync<HubException>(() => hub.JoinGroup(group));
            await groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>());
        }
    }
}
