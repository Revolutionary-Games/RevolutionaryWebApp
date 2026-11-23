namespace RevolutionaryWebApp.Server.Tests.Models.Tests;

using System;
using Server.Models;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class ModerationAccessTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void RestrictionsOverrideCachedPrivilegesAndExpireWithoutReplacingThem(bool pending, bool muted)
    {
        var user = new User("user@example.org", "User")
        {
            ModerationPending = pending,
            MutedUntil = muted ? DateTime.UtcNow.AddDays(1) : null,
        };
        var groups = new CachedUserGroups(GroupType.User, GroupType.Developer, GroupType.SourceTextTranslator);
        user.ForceResolveGroupsForTesting(groups);
        var session = new Session { UserId = 1, CachedUserGroups = groups };
        user.SetGroupsFromSessionCache(session);

        var restricted = user.AccessCachedGroupsOrThrow();
        Assert.True(restricted.HasAccessLevel(GroupType.RestrictedUser));
        Assert.False(restricted.HasAccessLevel(GroupType.User));
        Assert.False(restricted.HasGroup(GroupType.SourceTextTranslator));
        Assert.True(session.CachedUserGroups!.HasAccessLevel(GroupType.Developer));

        user.ModerationPending = false;
        user.MutedUntil = DateTime.UtcNow.AddSeconds(-1);
        Assert.True(user.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Developer));
        Assert.True(user.AccessCachedGroupsOrThrow().HasGroup(GroupType.SourceTextTranslator));
    }
}
