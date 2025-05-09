namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Jobs;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;

public static class PatreonGroupHandler
{
    public const string CommunityDevBuildGroup = "Supporter";
    public const string CommunityVIPGroup = "VIP_supporter";

    public enum RewardGroup
    {
        None,
        DevBuild,
        VIP,
    }

    public static async Task<bool> HandlePatreonPledgeObject(PatreonObjectData? pledge, PatreonObjectData? user,
        string? rewardId, NotificationsEnabledDb database, IBackgroundJobClient jobClient)
    {
        if (pledge?.Attributes.AmountCents == null || user?.Attributes.Email == null)
            throw new Exception("Invalid patron API object, missing key properties");

        if (rewardId == null)
            throw new Exception("Invalid patron API object, missing any reward id");

        var pledgeCents = pledge.Attributes.AmountCents.Value;

        bool declined = !string.IsNullOrEmpty(pledge.Attributes.DeclinedSince);

        var email = user.Attributes.Email?.Trim();

        if (string.IsNullOrEmpty(email))
            throw new Exception("Patron object has null email");

        var patron = await database.Patrons.FirstOrDefaultAsync(p => p.Email == email);

        var username = user.Attributes.Vanity;

        if (string.IsNullOrWhiteSpace(username))
            username = user.Attributes.FullName;

        if (string.IsNullOrWhiteSpace(username))
        {
            // TODO: to resolve already used name conflicts the Id could be appended here
            username = user.Attributes.FirstName;
        }

        // Ensure no trailing spaces in patron names
        username = username?.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            // Fallback to using the id if everything failed...
            username = $"Patron {user.Id}";
        }

        if (patron == null)
        {
            if (!declined)
            {
                await database.LogEntries.AddAsync(new LogEntry($"We have a new patron: {username}"));

                await database.Patrons.AddAsync(new Patron
                {
                    Username = username,
                    Email = email,
                    PledgeAmountCents = pledgeCents,
                    RewardId = rewardId,
                    Marked = true,
                });

                // Queue user group apply if there's an account
                // TODO: email aliases
                if (await database.Users.AnyAsync(u => u.Email == email))
                {
                    jobClient.Schedule<ApplyUserAutomaticGroupsJob>(x => x.Execute(email, CancellationToken.None),
                        TimeSpan.FromSeconds(90));
                }

                return true;
            }

            return false;
        }

        patron.Marked = true;

        bool changes = false;
        bool reApplyGroups = false;

        if (declined)
        {
            if (patron.Suspended != true)
            {
                await database.LogEntries.AddAsync(
                    new LogEntry($"A patron ({patron.Id}) is now in declined state. Setting as suspended"));

                patron.Suspended = true;
                patron.SuspendedReason = "Payment failed on Patreon";
                reApplyGroups = true;

                changes = true;
            }
        }
        else if (patron.RewardId != rewardId || patron.Username != username)
        {
            await database.LogEntries.AddAsync(new LogEntry($"A patron ({patron.Id}) has changed their reward or name",
                "Old name: " + patron.Username));

            patron.RewardId = rewardId;
            patron.PledgeAmountCents = pledgeCents;
            patron.Username = username;
            patron.Suspended = false;
            reApplyGroups = true;

            changes = true;
        }
        else if (patron.Suspended == true)
        {
            await database.LogEntries.AddAsync(
                new LogEntry($"A patron ({patron.Id}) is no longer declined on Patreon's side"));

            patron.Suspended = false;
            reApplyGroups = true;

            changes = true;
        }

        if (reApplyGroups)
        {
            // Need to wait for this job as the changes aren't saved immediately
            // TODO: email aliases
            jobClient.Schedule<ApplyUserAutomaticGroupsJob>(x => x.Execute(patron.Email, CancellationToken.None),
                TimeSpan.FromSeconds(90));
        }

        return changes;
    }

    public static RewardGroup ShouldBeInGroupForPatron(Patron? patron, PatreonSettings settings)
    {
        if (settings == null)
            throw new ArgumentException("patreon settings is null");

        if (patron == null || patron.Suspended == true)
            return RewardGroup.None;

        if (patron.RewardId == settings.VipRewardId)
            return RewardGroup.VIP;

        if (patron.RewardId == settings.DevbuildsRewardId)
            return RewardGroup.DevBuild;

        return RewardGroup.None;
    }
}
