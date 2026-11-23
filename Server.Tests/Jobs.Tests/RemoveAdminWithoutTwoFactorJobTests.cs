namespace RevolutionaryWebApp.Server.Tests.Jobs.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Server.Jobs;
using Server.Models;
using Shared.Models.Enums;
using Xunit;

public sealed class RemoveAdminWithoutTwoFactorJobTests
{
    [Fact]
    public async Task RemovesAdminFromUsersWithoutEnabledTwoFactorAndIsIdempotent()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ApplicationDbContext(options);
        var jobClient = Substitute.For<IBackgroundJobClient>();

        var adminGroup = new UserGroup(GroupType.Admin, "Admin");
        var developerGroup = new UserGroup(GroupType.Developer, "Developer");
        database.UserGroups.AddRange(adminGroup, developerGroup);
        await database.SaveChangesAsync();

        var withoutTwoFactor = new User("without-two-factor@example.com", "WithoutTwoFactor");
        withoutTwoFactor.Groups.Add(adminGroup);

        var withDisabledTwoFactor = new User("disabled-two-factor@example.com", "DisabledTwoFactor")
        {
            TwoFactorAuthentication = new TwoFactorAuthentication { Enabled = false },
        };
        withDisabledTwoFactor.Groups.Add(adminGroup);

        var withEnabledTwoFactor = new User("enabled-two-factor@example.com", "EnabledTwoFactor")
        {
            TwoFactorAuthentication = new TwoFactorAuthentication { Enabled = true },
        };
        withEnabledTwoFactor.Groups.Add(adminGroup);

        var nonAdmin = new User("non-admin@example.com", "NonAdmin");
        nonAdmin.Groups.Add(developerGroup);

        database.Users.AddRange(withoutTwoFactor, withDisabledTwoFactor, withEnabledTwoFactor, nonAdmin);
        await database.SaveChangesAsync();

        var job = new RemoveAdminWithoutTwoFactorJob(NullLogger<RemoveAdminWithoutTwoFactorJob>.Instance, database,
            jobClient);

        await job.Execute(CancellationToken.None);

        database.ChangeTracker.Clear();
        var users = await database.Users.AsNoTracking().Include(user => user.Groups)
            .ToDictionaryAsync(user => user.Email);

        Assert.DoesNotContain(users[withoutTwoFactor.Email].Groups, group => group.Id == GroupType.Admin);
        Assert.DoesNotContain(users[withDisabledTwoFactor.Email].Groups, group => group.Id == GroupType.Admin);
        Assert.Contains(users[withEnabledTwoFactor.Email].Groups, group => group.Id == GroupType.Admin);
        Assert.Contains(users[nonAdmin.Email].Groups, group => group.Id == GroupType.Developer);
        Assert.DoesNotContain(users[nonAdmin.Email].Groups, group => group.Id == GroupType.Admin);

        var actions = await database.AdminActions.AsNoTracking().ToListAsync();
        Assert.Equal(2, actions.Count);
        Assert.All(actions, action =>
        {
            Assert.Equal("Automatically removed from Admin group because two-factor authentication is not enabled",
                action.Message);
            Assert.Null(action.PerformedById);
        });
        Assert.Contains(actions, action => action.TargetUserId == withoutTwoFactor.Id);
        Assert.Contains(actions, action => action.TargetUserId == withDisabledTwoFactor.Id);
        jobClient.Received(2).Create(Arg.Is<Job>(scheduledJob =>
            scheduledJob.Type == typeof(UpdateUserGroupCacheJob)), Arg.Any<IState>());

        await job.Execute(CancellationToken.None);

        Assert.Equal(2, await database.AdminActions.CountAsync());
        jobClient.Received(2).Create(Arg.Is<Job>(scheduledJob =>
            scheduledJob.Type == typeof(UpdateUserGroupCacheJob)), Arg.Any<IState>());
    }
}
