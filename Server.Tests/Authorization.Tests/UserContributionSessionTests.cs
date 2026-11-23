namespace RevolutionaryWebApp.Server.Tests.Authorization.Tests;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Server.Authorization;
using Server.Models;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class UserContributionSessionTests
{
    [Fact]
    public async Task SessionUserDtoIncludesStoredContributionCount()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var database = new ApplicationDbContext(options);

        var adminGroup = new UserGroup(GroupType.Admin, "Admin");
        var contributingAdmin = new User("contributor@example.com", "contributor")
        {
            Groups = new List<UserGroup> { adminGroup },
        };
        var newAdmin = new User("new-admin@example.com", "new-admin")
        {
            Groups = new List<UserGroup> { adminGroup },
        };
        database.UserGroups.Add(adminGroup);
        database.Users.AddRange(contributingAdmin, newAdmin);
        await database.SaveChangesAsync();

        var contributingAdminSessionId = Guid.NewGuid();
        var newAdminSessionId = Guid.NewGuid();
        database.Sessions.AddRange(new Session
            {
                Id = contributingAdminSessionId,
                UserId = contributingAdmin.Id,
                User = contributingAdmin,
                CachedUserGroups = contributingAdmin.ProcessGroupDataFromLoadedGroups(),
            },
            new Session
            {
                Id = newAdminSessionId,
                UserId = newAdmin.Id,
                User = newAdmin,
                CachedUserGroups = newAdmin.ProcessGroupDataFromLoadedGroups(),
            });
        database.UserContributionCounts.Add(new UserContributionCount
        {
            UserId = contributingAdmin.Id,
            User = contributingAdmin,
            CurrentContribution = 1000,
            ContributionLimit = 100000,
        });
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var loadedSession = await UserFromCookiesHelper.GetSession(contributingAdminSessionId.ToString(),
            contributingAdmin.Id, database);
        var loadedUser = await UserFromCookiesHelper.GetUserFromSession(loadedSession, database, false);

        Assert.NotNull(loadedUser);
        var dto = loadedUser.GetDTO(RecordAccessLevel.Admin);
        Assert.Equal(1000, dto.CurrentContribution);
        Assert.Equal(100000, dto.ContributionLimit);

        var newAdminSession = await UserFromCookiesHelper.GetSession(newAdminSessionId.ToString(), newAdmin.Id,
            database);
        var newAdminUser = await UserFromCookiesHelper.GetUserFromSession(newAdminSession, database, false);

        Assert.NotNull(newAdminUser);
        var newAdminDto = newAdminUser.GetDTO(RecordAccessLevel.Admin);
        Assert.Equal(0, newAdminDto.CurrentContribution);
        Assert.Equal(100000, newAdminDto.ContributionLimit);
    }
}
