namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Utilities;

public abstract class PatreonForumGroupBaseJob
{
    protected readonly ApplicationDbContext Database;
    protected readonly ICommunityForumAPI DiscourseAPI;

    protected readonly List<string> UsernamesToRemoveFromDevBuild = new();
    protected readonly List<string> UsernamesToRemoveFromVIP = new();
    protected readonly List<string> UsernamesToAddToDevBuild = new();
    protected readonly List<string> UsernamesToAddToVIP = new();

    protected PatreonSettings? settings;
    protected DiscourseGroupMembers? devBuildGroupMembers;
    protected DiscourseGroupMembers? vipGroupMembers;

    protected PatreonForumGroupBaseJob(ApplicationDbContext database, ICommunityForumAPI discourseAPI)
    {
        Database = database;
        DiscourseAPI = discourseAPI;
    }

    protected async Task<bool> LoadSettingsOrSkip(ILogger logger, CancellationToken cancellationToken)
    {
        if (!DiscourseAPI.Configured)
        {
            logger.LogWarning("Community forum API unconfigured, skipping ApplyPatronForumGroupsJob");
            return false;
        }

        settings = await Database.PatreonSettings.OrderBy(p => p.Id).FirstOrDefaultAsync(cancellationToken);

        if (settings == null)
        {
            logger.LogWarning("Patreon settings unconfigured, skipping ApplyPatronForumGroupsJob");
            return false;
        }

        return true;
    }

    protected async Task ApplyGroupMemberChanges(ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("Devbuild add: {UsernamesToAddToDevBuild} remove: {UsernamesToRemoveFromDevBuild} " +
            "VIP add: {UsernamesToAddToVip} remove: {UsernamesToRemoveFromVip}",
            UsernamesToAddToDevBuild, UsernamesToRemoveFromDevBuild, UsernamesToAddToVIP, UsernamesToRemoveFromVIP);

        var devBuildGroup =
            await DiscourseAPI.GetGroupInfo(PatreonGroupHandler.CommunityDevBuildGroup, cancellationToken);
        var vipGroup = await DiscourseAPI.GetGroupInfo(PatreonGroupHandler.CommunityVIPGroup, cancellationToken);

        await DiscourseAPI.AddGroupMembers(devBuildGroup, UsernamesToAddToDevBuild, cancellationToken);
        await DiscourseAPI.AddGroupMembers(vipGroup, UsernamesToAddToVIP, cancellationToken);
        await DiscourseAPI.RemoveGroupMembers(devBuildGroup, UsernamesToRemoveFromDevBuild, cancellationToken);
        await DiscourseAPI.RemoveGroupMembers(vipGroup, UsernamesToRemoveFromVIP, cancellationToken);
    }

    protected async Task LoadDiscourseGroupMembers(CancellationToken cancellationToken)
    {
        devBuildGroupMembers =
            await DiscourseAPI.GetGroupMembers(PatreonGroupHandler.CommunityDevBuildGroup, cancellationToken);
        vipGroupMembers =
            await DiscourseAPI.GetGroupMembers(PatreonGroupHandler.CommunityVIPGroup, cancellationToken);
    }

    protected void HandlePatron(Patron patron, DiscourseUser correspondingForumUser, ILogger logger)
    {
        if (settings == null)
            throw new InvalidOperationException("Patreon settings haven't been loaded");

        if (devBuildGroupMembers == null || vipGroupMembers == null)
            throw new InvalidOperationException("Discourse group members have not been loaded");

        var username = correspondingForumUser.Username;

        logger.LogTrace("Handling ({Patron}) {Username}", patron.Username,
            username);

        var shouldBeGroup = PatreonGroupHandler.ShouldBeInGroupForPatron(patron, settings);

        logger.LogTrace("Target group {ShouldBeGroup}", shouldBeGroup);

        // Detect group adds and removes
        CheckSingleGroupAddRemove(username, devBuildGroupMembers,
            shouldBeGroup == PatreonGroupHandler.RewardGroup.DevBuild, UsernamesToRemoveFromDevBuild,
            UsernamesToAddToDevBuild);
        CheckSingleGroupAddRemove(username, vipGroupMembers, shouldBeGroup == PatreonGroupHandler.RewardGroup.VIP,
            UsernamesToRemoveFromVIP, UsernamesToAddToVIP);
    }

    private void CheckSingleGroupAddRemove(string username, DiscourseGroupMembers groupMembers,
        bool shouldBeInThisGroup,
        List<string> toRemove, List<string> toAdd)
    {
        // Find and mark the entries as used
        var existsInGroup = groupMembers.CheckMemberShipAndMark(username);
        var isOwner = groupMembers.IsOwner(username);

        // Remove from group if shouldn't be
        if (!shouldBeInThisGroup && existsInGroup && !isOwner)
        {
            toRemove.Add(username);
        }

        // And add if should be in this group
        if (shouldBeInThisGroup && !existsInGroup)
        {
            toAdd.Add(username);
        }
    }
}
