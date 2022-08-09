namespace ThriveDevCenter.Server.Jobs;

using System;
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
public class ApplyPatronForumGroupsJob : PatreonForumGroupBaseJob, IJob
{
    private readonly ILogger<ApplyPatronForumGroupsJob> logger;

    public ApplyPatronForumGroupsJob(ILogger<ApplyPatronForumGroupsJob> logger, ApplicationDbContext database,
        CommunityForumAPI discourseAPI) : base(database, discourseAPI)
    {
        this.logger = logger;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        if (!await LoadSettingsOrSkip(logger, cancellationToken))
            return;

        await LoadDiscourseGroupMembers(cancellationToken);
        if (DevBuildGroupMembers == null || VIPGroupMembers == null)
            throw new Exception("Failed to load discourse group members");

        await HandlePatrons(cancellationToken);

        logger.LogTrace("Checking extraneous group members");

        UsernamesToRemoveFromDevBuild.AddRange(DevBuildGroupMembers.GetUnmarkedMembers().AsEnumerable()
            .Select(m => m.Username));

        UsernamesToRemoveFromVIP.AddRange(VIPGroupMembers.GetUnmarkedMembers().AsEnumerable()
            .Select(m => m.Username));

        if (cancellationToken.IsCancellationRequested)
            return;

        await Database.SaveChangesAsync(cancellationToken);

        await ApplyGroupMemberChanges(logger, cancellationToken);
    }

    private async Task HandlePatrons(CancellationToken cancellationToken)
    {
        if (Settings == null)
            throw new InvalidOperationException("Patreon settings haven't been loaded");

        // TODO: might need to change this to working in batches
        var allPatrons = await Database.Patrons.ToListAsync(cancellationToken);

        foreach (var patron in allPatrons)
        {
            // Skip patrons who shouldn't have a forum group, check_unmarked will find them
            if (PatreonGroupHandler.ShouldBeInGroupForPatron(patron, Settings) ==
                PatreonGroupHandler.RewardGroup.None)
            {
                continue;
            }

            // Also skip suspended who should have their groups revoked as long as they are suspended
            if (patron.Suspended == true)
                continue;

            // TODO: alias implementation
            var forumUser = await DiscourseAPI.FindUserByEmail(patron.Email, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            if (forumUser == null)
            {
                logger.LogTrace("Patron ({Username}) is missing a forum account, can't apply groups",
                    patron.Username);
                patron.HasForumAccount = false;
            }
            else
            {
                patron.HasForumAccount = true;
                HandlePatron(patron, forumUser, logger);
            }
        }
    }
}