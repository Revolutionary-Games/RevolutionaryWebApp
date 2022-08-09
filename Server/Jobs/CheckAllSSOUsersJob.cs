namespace ThriveDevCenter.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Utilities;

/// <summary>
///   Checks all users that aren't local that the SSO data is still valid (if not they are suspended)
/// </summary>
[DisableConcurrentExecution(1200)]
public class CheckAllSSOUsersJob : IJob
{
    private readonly ILogger<CheckAllSSOUsersJob> logger;
    private readonly ApplicationDbContext database;
    private readonly CommunityForumAPI communityAPI;
    private readonly DevForumAPI devForumAPI;

    public CheckAllSSOUsersJob(ILogger<CheckAllSSOUsersJob> logger, ApplicationDbContext database,
        CommunityForumAPI communityAPI,
        DevForumAPI devForumAPI)
    {
        this.logger = logger;
        this.database = database;
        this.communityAPI = communityAPI;
        this.devForumAPI = devForumAPI;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        bool requiresSave = false;

        // TODO: even though batching (Buffer) could be used here, won't the database context keep things in memory?
        foreach (var user in await database.Users.ToListAsync(cancellationToken))
        {
            if (await SSOSuspendHandler.CheckUser(user, database, communityAPI, devForumAPI, logger,
                    cancellationToken))
                requiresSave = true;
        }

        if (requiresSave)
            await database.SaveChangesAsync(cancellationToken);
    }
}