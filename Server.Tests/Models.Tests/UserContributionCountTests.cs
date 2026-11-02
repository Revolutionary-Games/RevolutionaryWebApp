namespace RevolutionaryWebApp.Server.Tests.Models.Tests;

using System;
using Server.Filters;
using Server.Models;
using Shared.Models;
using Shared.Models.Enums;
using Xunit;

public class UserContributionCountTests
{
    [Theory]
    [InlineData(GroupType.User, 10000)]
    [InlineData(GroupType.PatreonSupporter, 15000)]
    [InlineData(GroupType.Developer, 30000)]
    [InlineData(GroupType.Admin, 100000)]
    public void InitialLimitFollowsGroup(GroupType group, int expected)
    {
        Assert.Equal(expected, UserContributionCount.GetInitialLimit(new CachedUserGroups(group)));
    }

    [Fact]
    public void HighestGroupWins()
    {
        Assert.Equal(100000, UserContributionCount.GetInitialLimit(
            new CachedUserGroups(GroupType.PatreonSupporter, GroupType.Developer, GroupType.Admin)));
    }

    [Theory]
    [InlineData(false, 10500)]
    [InlineData(true, 15000)]
    public void ActiveBudgetGrowsAndResets(bool exceeded, int expected)
    {
        var count = new UserContributionCount
        {
            ContributionLimit = 10000,
            CurrentContribution = 1,
            ContributionExceeded = exceeded,
        };
        var firstContribution = count.FirstContributedAt;
        count.ResetDaily(new CachedUserGroups(GroupType.User));
        Assert.Equal(expected, count.ContributionLimit);
        Assert.Equal(0, count.CurrentContribution);
        Assert.False(count.ContributionExceeded);
        Assert.Equal(firstContribution, count.FirstContributedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GrowthIsCapped(bool exceeded)
    {
        var count = new UserContributionCount
        {
            ContributionLimit = 99000,
            CurrentContribution = 1,
            ContributionExceeded = exceeded,
        };
        count.ResetDaily(new CachedUserGroups(GroupType.User));
        Assert.Equal(100000, count.ContributionLimit);
    }

    [Theory]
    [InlineData(GroupType.Admin, 150000)]
    [InlineData(GroupType.User, 100000)]
    public void ResetUsesCurrentGroupsAfterGroupChange(GroupType group, int expected)
    {
        var count = new UserContributionCount
        {
            ContributionLimit = 100000,
            CurrentContribution = 1,
            ContributionExceeded = true,
        };
        count.ResetDaily(new CachedUserGroups(group));
        Assert.Equal(expected, count.ContributionLimit);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public void ReachingLimitIsReportedOnce(int amount, bool expected)
    {
        var count = new UserContributionCount { ContributionLimit = 10 };
        Assert.Equal(expected, count.AddContribution(amount));
        Assert.Equal(amount, count.CurrentContribution);
        Assert.Equal(expected, count.ContributionExceeded);
        if (expected)
            Assert.False(count.AddContribution(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ContributionCostsMustBePositive(int amount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CountContributionAttribute(amount));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UserContributionCount().AddContribution(amount));
    }

    [Fact]
    public void InactiveBudgetDoesNotGrow()
    {
        var count = new UserContributionCount { ContributionLimit = 10000 };
        count.ResetDaily(new CachedUserGroups(GroupType.User));
        Assert.Equal(10000, count.ContributionLimit);
    }
}
