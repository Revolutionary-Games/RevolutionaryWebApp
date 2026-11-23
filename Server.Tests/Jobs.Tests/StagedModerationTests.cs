namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Server.Jobs;
using Server.Jobs.RegularlyScheduled;
using Server.Models;
using Server.Services;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class StagedModerationTests
{
    [Fact]
    public async Task StagingSchedulesBothPassesAndRecoveryFindsUnfinishedWork()
    {
        await using var database = CreateDatabase();
        var actor = AddUser(database, "Admin");
        var target = AddUser(database, "Target");
        await database.SaveChangesAsync();
        var jobs = Substitute.For<IBackgroundJobClient>();
        var calls = new List<(Job Job, ScheduledState State)>();
        jobs.When(client => client.Create(Arg.Any<Job>(), Arg.Any<IState>())).Do(call =>
        {
            if (call.Arg<IState>() is ScheduledState state)
                calls.Add((call.Arg<Job>(), state));
        });
        var moderation = CreateService(database, jobs);
        var cutoff = DateTime.UtcNow.AddHours(-48);
        await moderation.StageBan(actor, target, DateTime.MaxValue, "Spam", cutoff);
        await database.SaveChangesAsync();
        var before = DateTime.UtcNow;
        moderation.SchedulePendingActions();
        var action = await database.PendingUserModeration.SingleAsync();
        Assert.True(target.ModerationPending);
        Assert.False(target.Suspended);
        Assert.Equal(cutoff, action.PurgeCutoff);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Equal(typeof(ApplyUserModerationJob), call.Job.Type));
        Assert.False((bool)calls[0].Job.Args[1]);
        Assert.True((bool)calls[1].Job.Args[1]);
        Assert.InRange(calls[0].State.EnqueueAt, before.AddSeconds(5), DateTime.UtcNow.AddSeconds(5));
        Assert.InRange(calls[1].State.EnqueueAt, before.AddMinutes(4), DateTime.UtcNow.AddMinutes(4));

        action.CreatedAt = DateTime.UtcNow.AddMinutes(-5);
        await database.SaveChangesAsync();
        jobs.ClearReceivedCalls();
        await new RecoverPendingModerationJob(database, jobs).Execute(CancellationToken.None);
        jobs.Received(1).Create(Arg.Is<Job>(job => job.Type == typeof(ApplyUserModerationJob) &&
            (long)job.Args[0] == action.Id && !(bool)job.Args[1]), Arg.Any<EnqueuedState>());

        await moderation.ProcessPendingAction(action.Id, false, CancellationToken.None);
        jobs.ClearReceivedCalls();
        await new RecoverPendingModerationJob(database, jobs).Execute(CancellationToken.None);
        jobs.Received(1).Create(Arg.Is<Job>(job => (bool)job.Args[1]), Arg.Any<EnqueuedState>());
        await moderation.ProcessPendingAction(action.Id, true, CancellationToken.None);
        jobs.ClearReceivedCalls();
        await new RecoverPendingModerationJob(database, jobs).Execute(CancellationToken.None);
        jobs.DidNotReceive().Create(Arg.Any<Job>(), Arg.Any<IState>());
    }

    [Fact]
    public async Task ManualUnsuspensionCancelsSecondPassAndExistingMuteIsPreserved()
    {
        await using var database = CreateDatabase();
        var actor = AddUser(database, "Admin");
        var target = AddUser(database, "Target");
        var muteUntil = DateTime.UtcNow.AddDays(5);
        target.MutedUntil = muteUntil;
        await database.SaveChangesAsync();
        var moderation = CreateService(database, Substitute.For<IBackgroundJobClient>());
        await moderation.StageBan(actor, target, DateTime.UtcNow.AddDays(2), "Spam");
        await database.SaveChangesAsync();
        var action = await database.PendingUserModeration.SingleAsync();
        await moderation.ProcessPendingAction(action.Id, false, CancellationToken.None);
        Assert.True(target.Suspended);
        Assert.False(target.ModerationPending);
        Assert.Equal(muteUntil, target.MutedUntil);

        target.SuspendedUntil = null;
        await database.Sessions.AddAsync(new Session { UserId = target.Id });
        await database.SaveChangesAsync();
        await moderation.ProcessPendingAction(action.Id, true, CancellationToken.None);
        Assert.False(target.Suspended);
        Assert.Single(await database.Sessions.ToListAsync());
        Assert.NotNull(action.CleanedUpAt);
        Assert.Equal(muteUntil, target.MutedUntil);
    }

    private static NotificationsEnabledDb CreateDatabase()
    {
        return new NotificationsEnabledDb(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Substitute.For<IModelUpdateNotificationSender>());
    }

    private static User AddUser(NotificationsEnabledDb database, string name)
    {
        var user = new User($"{name}@example.org", name);
        user.ForceResolveGroupsForTesting(new CachedUserGroups(GroupType.User));
        database.Users.Add(user);
        return user;
    }

    private static ModerationService CreateService(NotificationsEnabledDb database, IBackgroundJobClient jobs)
    {
        return new ModerationService(database, new ConfigurationBuilder().Build(),
            new ModerationSessionService(database, Substitute.For<IHubContext<NotificationsHub, INotifications>>()),
            jobs);
    }
}
