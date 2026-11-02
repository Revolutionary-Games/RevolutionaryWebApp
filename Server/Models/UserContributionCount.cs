namespace RevolutionaryWebApp.Server.Models;

using System;
using System.ComponentModel.DataAnnotations;
using Shared.Models;
using Shared.Models.Enums;

/// <summary>
///   Lazily created daily contribution budget. Growth is capped using the user's current groups.
/// </summary>
public class UserContributionCount
{
    [Key]
    public long UserId { get; set; }

    public User User { get; set; } = null!;
    public int ContributionLimit { get; set; }
    public int CurrentContribution { get; set; }

    public bool ContributionExceeded { get; set; }
    public DateTime FirstContributedAt { get; set; } = DateTime.UtcNow;

    public static int GetInitialLimit(CachedUserGroups groups)
    {
        if (groups.HasAccessLevel(GroupType.Admin))
            return 100000;
        if (groups.HasAccessLevel(GroupType.Developer))
            return 30000;
        if (groups.HasGroup(GroupType.PatreonSupporter))
            return 15000;

        return 10000;
    }

    /// <summary>
    ///   Returns true on the first successful contribution that reaches the daily limit.
    /// </summary>
    public bool AddContribution(int amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(amount);
        CurrentContribution = checked(CurrentContribution + amount);
        if (CurrentContribution < ContributionLimit || ContributionExceeded)
            return false;

        ContributionExceeded = true;
        return true;
    }

    public void ResetDaily(CachedUserGroups groups)
    {
        if (CurrentContribution <= 0)
            return;

        ContributionLimit = Math.Min(GetInitialLimit(groups) * 10,
            (int)Math.Ceiling(ContributionLimit * (ContributionExceeded ? 1.5f : 1.05f)));
        CurrentContribution = 0;
        ContributionExceeded = false;
    }
}
