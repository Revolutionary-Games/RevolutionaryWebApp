namespace ThriveDevCenter.Server.Jobs
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Services;
    using Utilities;

    /// <summary>
    ///   Makes sure forum groups are up to date based on the Patron data
    /// </summary>
    [DisableConcurrentExecution(750)]
    public class ApplyPatronForumGroupsJob : IJob
    {
        private readonly ILogger<ApplyPatronForumGroupsJob> logger;
        private readonly ApplicationDbContext database;
        private readonly CommunityForumAPI discourseAPI;

        private PatreonSettings settings;
        private DiscourseGroupMembers devBuildGroupMembers;
        private DiscourseGroupMembers vipGroupMembers;

        private readonly List<string> usernamesToRemoveFromDevBuild = new();
        private readonly List<string> usernamesToRemoveFromVIP = new();
        private readonly List<string> usernamesToAddToDevBuild = new();
        private readonly List<string> usernamesToAddToVIP = new();

        public ApplyPatronForumGroupsJob(ILogger<ApplyPatronForumGroupsJob> logger, ApplicationDbContext database,
            CommunityForumAPI discourseAPI)
        {
            this.logger = logger;
            this.database = database;
            this.discourseAPI = discourseAPI;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            if (!discourseAPI.Configured)
            {
                logger.LogWarning("Community forum API unconfigured, skipping ApplyPatronForumGroupsJob");
                return;
            }

            settings = await EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(database.PatreonSettings,
                cancellationToken);

            if (settings == null)
            {
                logger.LogWarning("Patreon settings unconfigured, skipping ApplyPatronForumGroupsJob");
                return;
            }

            devBuildGroupMembers =
                await discourseAPI.GetGroupMembers(PatreonGroupHandler.CommunityDevBuildGroup, cancellationToken);
            vipGroupMembers =
                await discourseAPI.GetGroupMembers(PatreonGroupHandler.CommunityVIPGroup, cancellationToken);

            await HandlePatrons(cancellationToken);

            logger.LogInformation("Checking extraneous group members");

            usernamesToRemoveFromDevBuild.AddRange(devBuildGroupMembers.GetUnmarkedMembers().AsEnumerable()
                .Select(m => m.Username));

            usernamesToRemoveFromVIP.AddRange(vipGroupMembers.GetUnmarkedMembers().AsEnumerable()
                .Select(m => m.Username));

            if (cancellationToken.IsCancellationRequested)
                return;

            await database.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Devbuild add: {UsernamesToAddToDevBuild} remove: {UsernamesToRemoveFromDevBuild} " +
                "VIP add: {UsernamesToAddToVip} remove: {UsernamesToRemoveFromVip}",
                usernamesToAddToDevBuild, usernamesToRemoveFromDevBuild, usernamesToAddToVIP, usernamesToRemoveFromVIP);

            var devBuildGroup = await discourseAPI.GetGroupInfo(PatreonGroupHandler.CommunityDevBuildGroup, cancellationToken);
            var vipGroup = await discourseAPI.GetGroupInfo(PatreonGroupHandler.CommunityVIPGroup, cancellationToken);

            await discourseAPI.AddGroupMembers(devBuildGroup, usernamesToAddToDevBuild, cancellationToken);
            await discourseAPI.AddGroupMembers(vipGroup, usernamesToAddToVIP, cancellationToken);
            await discourseAPI.RemoveGroupMembers(devBuildGroup, usernamesToRemoveFromDevBuild, cancellationToken);
            await discourseAPI.RemoveGroupMembers(vipGroup, usernamesToRemoveFromVIP, cancellationToken);
        }

        private async Task HandlePatrons(CancellationToken cancellationToken)
        {
            // TODO: might need to change this to working in batches
            var allPatrons = await EntityFrameworkQueryableExtensions.ToListAsync(database.Patrons, cancellationToken);

            foreach (var patron in allPatrons)
            {
                // Skip patrons who shouldn't have a forum group, check_unmarked will find them
                if (PatreonGroupHandler.ShouldBeInGroupForPatron(patron, settings) ==
                    PatreonGroupHandler.RewardGroup.None)
                {
                    continue;
                }

                // Also skip suspended who should have their groups revoked as long as they are suspended
                if (patron.Suspended == true)
                    continue;

                // TODO: alias implementation
                var forumUser = await discourseAPI.FindUserByEmail(patron.Email, cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                    return;

                if (forumUser == null)
                {
                    // TODO: change to trace
                    logger.LogInformation("Patron ({Username}) is missing a forum account, can't apply groups",
                        patron.Username);
                    patron.HasForumAccount = false;
                }
                else
                {
                    patron.HasForumAccount = true;
                    HandlePatron(patron, forumUser);
                }
            }
        }

        private void HandlePatron(Patron patron, DiscourseUser correspondingForumUser)
        {
            var username = correspondingForumUser.Username;

            // TODO: change these to trace calls once this is confirmed working
            logger.LogInformation("Handling ({Patron}) {Username}", patron.Username,
                username);

            var shouldBeGroup = PatreonGroupHandler.ShouldBeInGroupForPatron(patron, settings);

            logger.LogInformation("Target group {ShouldBeGroup}", shouldBeGroup);

            // Detect group adds and removes
            CheckSingleGroupAddRemove(username, devBuildGroupMembers,
                shouldBeGroup == PatreonGroupHandler.RewardGroup.DevBuild, usernamesToRemoveFromDevBuild,
                usernamesToAddToDevBuild);
            CheckSingleGroupAddRemove(username, vipGroupMembers, shouldBeGroup == PatreonGroupHandler.RewardGroup.VIP,
                usernamesToRemoveFromVIP, usernamesToAddToVIP);
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

        /// <summary>
        ///   Sets the forum account status for a patron. TODO: probably remove
        /// </summary>
        /// <returns>True if changes need saving</returns>
        private bool SetForumAccountStatus(Patron patron, bool hasAccount)
        {
            if (patron.HasForumAccount == hasAccount)
                return false;

            patron.HasForumAccount = hasAccount;
            return true;
        }
    }
}
