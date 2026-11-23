namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Shared.Models.Enums;
using Xunit;

public class ModerationQueueJobTests
{
    [Theory]
    [InlineData(7, 0)]
    [InlineData(8, 1)]
    [InlineData(25, 3)]
    public async Task RemindersEscalateAndDoNotRepeatWhileUnread(int age, int expected)
    {
        await using var database = CreateDatabase();
        await AddModerators(database);
        await database.UserReports.AddAsync(new UserReport { CreatedAt = DateTime.UtcNow.AddHours(-age) });
        await database.SaveChangesAsync();
        var job = new NotifyModerationQueueJob(database);
        await job.Execute(CancellationToken.None);
        Assert.Equal(expected, await database.UserNotifications.CountAsync());
        await job.Execute(CancellationToken.None);
        Assert.Equal(expected, await database.UserNotifications.CountAsync());
        if (age >= 24)
        {
            var notice = await database.UserNotifications.FirstAsync();
            notice.ReadAt = DateTime.UtcNow;
            await database.SaveChangesAsync();
            await job.Execute(CancellationToken.None);
            Assert.Equal(expected, await database.UserNotifications.CountAsync());
            notice.CreatedAt = DateTime.UtcNow.AddHours(-25);
            await database.SaveChangesAsync();
            await job.Execute(CancellationToken.None);
            Assert.Equal(expected + 1, await database.UserNotifications.CountAsync());
        }
    }

    [Fact]
    public async Task RandomReminderDoesNotSuppressEscalationToOtherModerators()
    {
        await using var database = CreateDatabase();
        await AddModerators(database);
        var report = new UserReport { CreatedAt = DateTime.UtcNow.AddHours(-9) };
        await database.UserReports.AddAsync(report);
        await database.SaveChangesAsync();
        var job = new NotifyModerationQueueJob(database);
        await job.Execute(CancellationToken.None);
        Assert.Single(await database.UserNotifications.ToListAsync());
        report.CreatedAt = DateTime.UtcNow.AddHours(-25);
        await database.SaveChangesAsync();
        await job.Execute(CancellationToken.None);
        Assert.Equal(3, await database.UserNotifications.Select(item => item.UserId).Distinct().CountAsync());
        Assert.Equal(3, await database.UserNotifications.CountAsync());
        report.ResolvedAt = DateTime.UtcNow;
        await database.SaveChangesAsync();
        await job.Execute(CancellationToken.None);
        Assert.Equal(3, await database.UserNotifications.CountAsync());
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task DiscordOnlyPostsForConfiguredNonEmptyQueue(bool empty, bool disabled, bool posts)
    {
        await using var database = CreateDatabase();
        if (!empty)
        {
            await database.UserReports.AddAsync(new UserReport { CreatedAt = DateTime.UtcNow.AddHours(-9) });
            await database.SaveChangesAsync();
        }

        var jobs = Substitute.For<IBackgroundJobClient>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Discord:ModerationQueue"] = disabled ? null : "https://example.org/webhook",
            ["BaseUrl"] = "https://example.org/",
        }).Build();
        await new ReportModerationQueueJob(database, configuration, jobs).Execute(CancellationToken.None);
        if (posts)
        {
            jobs.Received(1).Create(Arg.Is<Job>(job =>
                    job.Args[1].ToString()!.Contains("https://example.org/moderation")),
                Arg.Any<IState>());
        }
        else
        {
            jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
        }
    }

    private static ApplicationDbContext CreateDatabase()
    {
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    private static async Task AddModerators(ApplicationDbContext database)
    {
        var moderator = new UserGroup(GroupType.ContentModerator, "Moderators");
        var admin = new UserGroup(GroupType.Admin, "Admins");
        for (var id = 1; id <= 3; ++id)
        {
            var user = new User($"mod{id}@example.org", $"Mod{id}");
            user.Groups.Add(id == 1 ? admin : moderator);
            user.ProcessGroupDataFromLoadedGroups();
            await database.Users.AddAsync(user);
        }

        await database.SaveChangesAsync();
    }
}
