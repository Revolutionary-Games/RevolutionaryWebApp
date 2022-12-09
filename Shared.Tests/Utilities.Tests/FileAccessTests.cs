namespace ThriveDevCenter.Shared.Tests.Utilities.Tests;

using DevCenterCommunication.Models.Enums;
using Shared.Models.Enums;
using Shared.Utilities;
using Xunit;

public class FileAccessTests
{
    [Fact]
    public void FileAccessTests_AdminValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.Admin, 1, null));
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.Admin, 1, 1));
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.Admin, 1, 2));

        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.Admin, 1, null));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.Admin, 1, 1));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.Admin, 1, 2));

        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.Admin, 1, null));
        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.Admin, 1, 1));
        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.Admin, 1, 2));

        Assert.True(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.Admin, 1, null));
        Assert.True(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.Admin, 1, 1));
        Assert.True(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.Admin, 1, 2));

        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.Admin, 1, null));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.Admin, 1, 1));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.Admin, 1, 2));

        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.Admin, 1, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.Admin, 1, 1));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.Admin, 1, 2));
    }

    [Fact]
    public void FileAccessTests_DeveloperValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.Developer, 1, null));
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.Developer, 1, 1));
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.Developer, 1, 2));

        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.Developer, 1, null));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.Developer, 1, 1));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.Developer, 1, 2));

        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.Developer, 1, null));
        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.Developer, 1, 1));
        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.Developer, 1, 2));

        Assert.True(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.Developer, 1, null));
        Assert.True(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.Developer, 1, 1));
        Assert.True(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.Developer, 1, 2));

        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.Developer, 1, null));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.Developer, 1, 1));
        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.Developer, 1, 2));

        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.Developer, 1, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.Developer, 1, 1));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.Developer, 1, 2));
    }

    [Fact]
    public void FileAccessTests_UserValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.User, 1, null));
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.User, 1, 1));
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.User, 1, 2));

        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.User, 1, null));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.User, 1, 1));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.User, 1, 2));

        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.User, 1, null));
        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.User, 1, 1));
        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.User, 1, 2));

        Assert.False(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.User, 1, null));
        Assert.True(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.User, 1, 1));
        Assert.False(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.User, 1, 2));

        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.User, 1, null));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.User, 1, 1));
        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.User, 1, 2));

        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.User, 1, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.User, 1, 1));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.User, 1, 2));
    }

    [Fact]
    public void FileAccessTests_RestrictedUserValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, null));
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 1));
        Assert.True(FileAccess.Public.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 2));

        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, null));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 1));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 2));

        Assert.False(FileAccess.User.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, null));
        Assert.True(FileAccess.User.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 1));
        Assert.False(FileAccess.User.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 2));

        Assert.False(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, null));
        Assert.True(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 1));
        Assert.False(FileAccess.Developer.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 2));

        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, null));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 1));
        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 2));

        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 1));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(UserAccessLevel.RestrictedUser, 1, 2));
    }

    [Fact]
    public void FileAccessTests_NotLoggedInValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(null, null, null));
        Assert.False(FileAccess.RestrictedUser.IsAccessibleTo(null, null, null));
        Assert.False(FileAccess.User.IsAccessibleTo(null, null, null));
        Assert.False(FileAccess.Developer.IsAccessibleTo(null, null, null));
        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(null, null, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(null, null, null));
    }
}
