namespace ThriveDevCenter.Shared.Tests.Models.Tests;

using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class UserAccessLevelTests
{
    [Fact]
    public void UserAccessLevel_AdminValuesWork()
    {
        Assert.True(new CachedUserGroups(GroupType.Admin).HasAccessLevel(GroupType.Admin));
        Assert.True(new CachedUserGroups(GroupType.Admin).HasAccessLevel(GroupType.Developer));
        Assert.True(new CachedUserGroups(GroupType.Admin).HasAccessLevel(GroupType.User));
        Assert.True(new CachedUserGroups(GroupType.Admin).HasAccessLevel(GroupType.RestrictedUser));
        Assert.True(new CachedUserGroups(GroupType.Admin).HasAccessLevel(GroupType.NotLoggedIn));
    }

    [Fact]
    public void UserAccessLevel_DeveloperValuesWork()
    {
        Assert.False(new CachedUserGroups(GroupType.Developer).HasAccessLevel(GroupType.Admin));
        Assert.True(new CachedUserGroups(GroupType.Developer).HasAccessLevel(GroupType.Developer));
        Assert.True(new CachedUserGroups(GroupType.Developer).HasAccessLevel(GroupType.User));
        Assert.True(new CachedUserGroups(GroupType.Developer).HasAccessLevel(GroupType.RestrictedUser));
        Assert.True(new CachedUserGroups(GroupType.Developer).HasAccessLevel(GroupType.NotLoggedIn));
    }

    [Fact]
    public void UserAccessLevel_UserValuesWork()
    {
        Assert.False(new CachedUserGroups(GroupType.User).HasAccessLevel(GroupType.Admin));
        Assert.False(new CachedUserGroups(GroupType.User).HasAccessLevel(GroupType.Developer));
        Assert.True(new CachedUserGroups(GroupType.User).HasAccessLevel(GroupType.User));
        Assert.True(new CachedUserGroups(GroupType.User).HasAccessLevel(GroupType.RestrictedUser));
        Assert.True(new CachedUserGroups(GroupType.User).HasAccessLevel(GroupType.NotLoggedIn));
    }

    [Fact]
    public void UserAccessLevel_RestrictedUserValuesWork()
    {
        Assert.False(new CachedUserGroups(GroupType.RestrictedUser).HasAccessLevel(GroupType.Admin));
        Assert.False(new CachedUserGroups(GroupType.RestrictedUser).HasAccessLevel(GroupType.Developer));
        Assert.False(new CachedUserGroups(GroupType.RestrictedUser).HasAccessLevel(GroupType.User));
        Assert.True(new CachedUserGroups(GroupType.RestrictedUser).HasAccessLevel(GroupType.RestrictedUser));
        Assert.True(new CachedUserGroups(GroupType.RestrictedUser).HasAccessLevel(GroupType.NotLoggedIn));
    }

    [Fact]
    public void UserAccessLevel_NotLoggedInValuesWork()
    {
        Assert.False(new CachedUserGroups(GroupType.NotLoggedIn).HasAccessLevel(GroupType.Admin));
        Assert.False(new CachedUserGroups(GroupType.NotLoggedIn).HasAccessLevel(GroupType.Developer));
        Assert.False(new CachedUserGroups(GroupType.NotLoggedIn).HasAccessLevel(GroupType.User));
        Assert.False(new CachedUserGroups(GroupType.NotLoggedIn).HasAccessLevel(GroupType.RestrictedUser));
        Assert.True(new CachedUserGroups(GroupType.NotLoggedIn).HasAccessLevel(GroupType.NotLoggedIn));
    }
}
