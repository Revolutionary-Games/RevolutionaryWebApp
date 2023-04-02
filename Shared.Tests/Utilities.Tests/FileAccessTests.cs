namespace ThriveDevCenter.Shared.Tests.Utilities.Tests;

using DevCenterCommunication.Models.Enums;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Utilities;
using Xunit;

public class FileAccessTests
{
    [Fact]
    public void FileAccessTests_AdminValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, null));
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 1));
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 2));

        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, null));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 1));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 2));

        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, null));
        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 1));
        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 2));

        Assert.True(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, null));
        Assert.True(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 1));
        Assert.True(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 2));

        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, null));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 1));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 2));

        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 1));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.Admin), 1, 2));
    }

    [Fact]
    public void FileAccessTests_DeveloperValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, null));
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 1));
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 2));

        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, null));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 1));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 2));

        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, null));
        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 1));
        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 2));

        Assert.True(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, null));
        Assert.True(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 1));
        Assert.True(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 2));

        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, null));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 1));
        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 2));

        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 1));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.Developer), 1, 2));
    }

    [Fact]
    public void FileAccessTests_UserValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, null));
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 1));
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 2));

        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, null));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 1));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 2));

        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, null));
        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 1));
        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 2));

        Assert.False(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, null));
        Assert.True(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 1));
        Assert.False(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 2));

        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, null));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 1));
        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 2));

        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 1));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.User), 1, 2));
    }

    [Fact]
    public void FileAccessTests_RestrictedUserValuesWork()
    {
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, null));
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 1));
        Assert.True(FileAccess.Public.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 2));

        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, null));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 1));
        Assert.True(FileAccess.RestrictedUser.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 2));

        Assert.False(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, null));
        Assert.True(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 1));
        Assert.False(FileAccess.User.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 2));

        Assert.False(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, null));
        Assert.True(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 1));
        Assert.False(FileAccess.Developer.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 2));

        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, null));
        Assert.True(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 1));
        Assert.False(FileAccess.OwnerOrAdmin.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 2));

        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, null));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 1));
        Assert.False(FileAccess.Nobody.IsAccessibleTo(new CachedUserGroups(GroupType.RestrictedUser), 1, 2));
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
