namespace ThriveDevCenter.Server.Tests.Models.Tests;

using System;
using System.Linq;
using System.Threading.Tasks;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Server.Models;
using Server.Utilities;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class SessionTests
{
    [Fact]
    public async Task Session_AutomaticallyCreatesHashedIdOnSave()
    {
        var database = new EditableInMemoryDatabaseFixture("SessionCreateNewHash");

        var session = new Session
        {
            SsoNonce = "1234",
        };

        await database.Database.Sessions.AddAsync(session);
        await database.Database.SaveChangesAsync();

        Assert.NotNull(session.HashedId);
        Assert.Equal(SelectByHashedProperty.HashForDatabaseValue(session.Id.ToString()), session.HashedId);

        var searched = await database.Database.Sessions.FirstAsync();

        Assert.Equal(session.Id, searched.Id);
        Assert.Equal(session.SsoNonce, searched.SsoNonce);
    }

    [Fact]
    public void Session_NoUserCachedGroupsIsEmpty()
    {
        var session = new Session();

        Assert.NotNull(session.CachedUserGroups);
        Assert.Null(session.CachedUserGroupsRaw);
        Assert.True(session.CachedUserGroups.HasAccessLevel(GroupType.NotLoggedIn));
        Assert.False(session.CachedUserGroups.HasAccessLevel(GroupType.Admin));
        Assert.Equal(GroupType.NotLoggedIn, session.CachedUserGroups.ComputePrimaryGroup());
    }

    [Fact]
    public void Session_UserCachedGroupsIsNotEmpty()
    {
        var session = new Session
        {
            UserId = 123,
            CachedUserGroups = new CachedUserGroups(GroupType.User),
        };

        Assert.NotNull(session.CachedUserGroups);
        Assert.True(session.CachedUserGroups.HasAccessLevel(GroupType.NotLoggedIn));
        Assert.True(session.CachedUserGroups.HasAccessLevel(GroupType.User));
        Assert.False(session.CachedUserGroups.HasAccessLevel(GroupType.Admin));
        Assert.Equal(GroupType.User, session.CachedUserGroups.ComputePrimaryGroup());
    }

    [Fact]
    public void Session_CachedGroupsSerializationWorks()
    {
        var originalGroups = new CachedUserGroups(GroupType.User, GroupType.Admin);

        var session = new Session
        {
            UserId = 1,
            CachedUserGroups = originalGroups,
        };

        Assert.Equal(@"{""Groups"":[2,4]}", session.CachedUserGroupsRaw);

        var returnedGroups = session.CachedUserGroups;

        Assert.False(ReferenceEquals(originalGroups, returnedGroups));

        Assert.True(originalGroups.Groups.SequenceEqual(returnedGroups.Groups));
        Assert.Equal(originalGroups.ComputePrimaryGroup(), returnedGroups.ComputePrimaryGroup());
    }

    [Fact]
    public void Session_GuidToStringCanBeParsedBack()
    {
        var guid = Guid.NewGuid();

        var asString = guid.ToString();

        var parsed = Guid.Parse(asString);

        Assert.Equal(guid, parsed);
    }
}
