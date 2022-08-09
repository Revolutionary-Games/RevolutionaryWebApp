namespace ThriveDevCenter.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;

[DisableConcurrentExecution(300)]
public class ApplySinglePatronGroupsJob : PatreonForumGroupBaseJob
{
    private readonly ILogger<ApplySinglePatronGroupsJob> logger;

    public ApplySinglePatronGroupsJob(ILogger<ApplySinglePatronGroupsJob> logger, ApplicationDbContext database,
        CommunityForumAPI discourseAPI) : base(database, discourseAPI)
    {
        this.logger = logger;
    }

    public async Task Execute(string email, CancellationToken cancellationToken)
    {
        var patron = await Database.Patrons.FirstOrDefaultAsync(p => p.Email == email, cancellationToken);

        // TODO: alias handling
        var forumEmail = patron?.Email ?? email;

        var forumUser = await DiscourseAPI.FindUserByEmail(forumEmail, cancellationToken);

        if (forumUser == null)
        {
            logger.LogInformation("Single user has no forum account, no handling needed for groups");

            // TODO: maybe re-queueing a check in 15 minutes should be done once
            return;
        }

        if (!await LoadSettingsOrSkip(logger, cancellationToken))
        {
            logger.LogWarning("Skipping Single patron groups apply because patreon settings are missing");
            return;
        }

        logger.LogInformation("Applying forum groups for single patron ({Username})", forumUser.Username);

        // See the TODO comment below as to why this is not needed currently
        /*
        var forumUserFull = await DiscourseAPI.UserInfoByName(forumUser.Username, cancellationToken);

        if (forumUserFull?.User == null)
            throw new Exception("Failed to find user group info after finding user object on the forums");
        */

        // TODO: this is highly inefficient, but there doesn't seem to be an API to get just group owners.
        // So we can use the existing code
        await LoadDiscourseGroupMembers(cancellationToken);

        // Bit of a hack, but the rest of the code doesn't need changes this way
        // When a patron is deleted, this job runs with just an email but no patron object so make one here
        patron ??= new Patron
        {
            Username = forumUser.Username,
            Email = forumEmail,
            RewardId = "none",
        };

        HandlePatron(patron, forumUser, logger);

        await ApplyGroupMemberChanges(logger, cancellationToken);
    }
}