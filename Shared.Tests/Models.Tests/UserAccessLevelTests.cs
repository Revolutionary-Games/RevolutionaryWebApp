namespace ThriveDevCenter.Shared.Tests.Models.Tests;

using Shared.Models.Enums;
using Xunit;

public class UserAccessLevelTests
{
    [Fact]
    public void UserAccessLevel_AdminValuesWork()
    {
        Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.Admin));
        Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.Developer));
        Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.User));
        Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.RestrictedUser));
        Assert.True(UserAccessLevel.Admin.HasAccess(UserAccessLevel.NotLoggedIn));
    }

    [Fact]
    public void UserAccessLevel_DeveloperValuesWork()
    {
        Assert.False(UserAccessLevel.Developer.HasAccess(UserAccessLevel.Admin));
        Assert.True(UserAccessLevel.Developer.HasAccess(UserAccessLevel.Developer));
        Assert.True(UserAccessLevel.Developer.HasAccess(UserAccessLevel.User));
        Assert.True(UserAccessLevel.Developer.HasAccess(UserAccessLevel.RestrictedUser));
        Assert.True(UserAccessLevel.Developer.HasAccess(UserAccessLevel.NotLoggedIn));
    }

    [Fact]
    public void UserAccessLevel_UserValuesWork()
    {
        Assert.False(UserAccessLevel.User.HasAccess(UserAccessLevel.Admin));
        Assert.False(UserAccessLevel.User.HasAccess(UserAccessLevel.Developer));
        Assert.True(UserAccessLevel.User.HasAccess(UserAccessLevel.User));
        Assert.True(UserAccessLevel.User.HasAccess(UserAccessLevel.RestrictedUser));
        Assert.True(UserAccessLevel.User.HasAccess(UserAccessLevel.NotLoggedIn));
    }

    [Fact]
    public void UserAccessLevel_RestrictedUserValuesWork()
    {
        Assert.False(UserAccessLevel.RestrictedUser.HasAccess(UserAccessLevel.Admin));
        Assert.False(UserAccessLevel.RestrictedUser.HasAccess(UserAccessLevel.Developer));
        Assert.False(UserAccessLevel.RestrictedUser.HasAccess(UserAccessLevel.User));
        Assert.True(UserAccessLevel.RestrictedUser.HasAccess(UserAccessLevel.RestrictedUser));
        Assert.True(UserAccessLevel.RestrictedUser.HasAccess(UserAccessLevel.NotLoggedIn));
    }

    [Fact]
    public void UserAccessLevel_NotLoggedInValuesWork()
    {
        Assert.False(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.Admin));
        Assert.False(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.Developer));
        Assert.False(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.User));
        Assert.False(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.RestrictedUser));
        Assert.True(UserAccessLevel.NotLoggedIn.HasAccess(UserAccessLevel.NotLoggedIn));
    }
}
