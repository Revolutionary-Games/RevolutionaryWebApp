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
using Server.Controllers;
using Server.Models;
using Server.Services;
using Xunit;

public class MentionSuggestionTests
{
    [Fact]
    public async Task SuggestionsPreferPrefixesAndLimitContainsMatches()
    {
        await using var database = new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            Substitute.For<IModelUpdateNotificationSender>());
        for (int i = 0; i < 15; ++i)
            database.Users.Add(new User($"user{i}@example.com", $"containsABC{i:00}"));
        database.Users.Add(new User("prefix@example.com", "abcPrefix"));
        database.Users.Add(new User("other@example.com", "unrelated"));
        await database.SaveChangesAsync();
        var controller = new UserManagementController(Substitute.For<ILogger<UserManagementController>>(), database,
            Substitute.For<IHubContext<NotificationsHub, INotifications>>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = (await controller.GetMentionSuggestions("AbC")).Value!;
        Assert.Equal(10, result.Count);
        Assert.Equal("abcPrefix", result[0]);
        Assert.All(result, name => Assert.Contains("abc", name.ToLowerInvariant()));
        Assert.IsType<BadRequestObjectResult>((await controller.GetMentionSuggestions("ab")).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.GetMentionSuggestions("abc%_")).Result);
    }
}
