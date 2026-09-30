namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Jobs;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models;

public static class PatreonGroupHandler
{
    public const string SyntheticEmailDomain = "revolutionarygamesstudio.com";
    public const string CommunityDevBuildGroup = "Supporter";
    public const string CommunityVIPGroup = "VIP_supporter";

    public enum RewardGroup
    {
        None,
        DevBuild,
        VIP,
    }

    public static bool IsSyntheticEmail(string? email)
    {
        return !string.IsNullOrEmpty(email) &&
            email.StartsWith("noreply+patron-", StringComparison.OrdinalIgnoreCase) &&
            email.EndsWith("@" + SyntheticEmailDomain, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<bool> HandlePatreonMemberObject(PatreonObjectData? member, PatreonObjectData? user,
        IReadOnlyCollection<PatreonObjectData> entitledTiers, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        if (member == null)
            throw new Exception("Invalid patron API object, missing key properties");

        if (user == null)
            throw new Exception("Invalid patron API object, missing user");

        // For some reason some users do not have any email associated with them, but otherwise are fine
        var memberEmail = member.Attributes.Email;
        if (!string.IsNullOrEmpty(memberEmail))
            user.Attributes.Email = memberEmail;

        if (user.Attributes.Email == null)
        {
            if (user.Attributes.FullName == null)
                throw new Exception("Invalid patron API object, missing key properties");

            user.Attributes.Email = $"noreply+patron-{Uri.EscapeDataString(user.Id)}@{SyntheticEmailDomain}";
        }

        var pledgeCents = member.Attributes.CurrentlyEntitledAmountCents ?? 0;

        bool declined = !string.Equals(member.Attributes.PatronStatus, "active_patron",
            StringComparison.OrdinalIgnoreCase);

        var email = user.Attributes.Email?.Trim();

        if (string.IsNullOrEmpty(email))
            throw new Exception("Patron object has null email");

        var tierIds = entitledTiers.Select(tier => tier.Id).Distinct(StringComparer.Ordinal).ToArray();
        var selectedTierId = tierIds.FirstOrDefault() ?? string.Empty;
        var patron = await database.Patrons.FirstOrDefaultAsync(p =>
            p.PatreonMemberId == member.Id || p.PatreonUserId == user.Id || p.Email == email);

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
                    TierId = selectedTierId,
                    EntitledTierIds = string.Join(',', tierIds),
                    PatreonMemberId = member.Id,
                    PatreonUserId = user.Id,
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
        else if (patron.TierId != selectedTierId || patron.EntitledTierIds != string.Join(',', tierIds) ||
                 patron.Username != username || patron.PledgeAmountCents != pledgeCents ||
                 patron.PatreonMemberId != member.Id || patron.PatreonUserId != user.Id)
        {
            await database.LogEntries.AddAsync(new LogEntry($"A patron ({patron.Id}) has changed their reward or name",
                "Old name: " + patron.Username));

            patron.TierId = selectedTierId;
            patron.EntitledTierIds = string.Join(',', tierIds);
            patron.PatreonMemberId = member.Id;
            patron.PatreonUserId = user.Id;
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

        if (settings.IsEntitledToVIP(patron))
            return RewardGroup.VIP;

        if (settings.IsEntitledToDevBuilds(patron))
            return RewardGroup.DevBuild;

        return RewardGroup.None;
    }
}
