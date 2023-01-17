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
///   Checks all users that aren't local that the SSO data is still valid (if not they are suspended)
/// </summary>
[DisableConcurrentExecution(1200)]
public class CheckAllSSOUsersJob : IJob
{
    private readonly ILogger<CheckAllSSOUsersJob> logger;
    private readonly ApplicationDbContext database;
    private readonly ICommunityForumAPI communityAPI;
    private readonly IDevForumAPI devForumAPI;

    public CheckAllSSOUsersJob(ILogger<CheckAllSSOUsersJob> logger, ApplicationDbContext database,
        ICommunityForumAPI communityAPI, IDevForumAPI devForumAPI)
    {
        this.logger = logger;
        this.database = database;
        this.communityAPI = communityAPI;
        this.devForumAPI = devForumAPI;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        bool requiresSave = false;

        // As users are async enumerated, we need to fetch the settings first
        var patreonSettings = await database.PatreonSettings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);

        var patreonSettingsRetriever = new Lazy<Task<PatreonSettings>>(() =>
            Task.FromResult(patreonSettings ?? throw new InvalidOperationException("PatreonSettings not available")));

        // TODO: even though batching (Buffer) could be used here, won't the database context keep things in memory?
        foreach (var user in await database.Users.ToListAsync(cancellationToken))
        {
            if (await SSOSuspendHandler.CheckUser(user, database, communityAPI, devForumAPI, logger,
                    patreonSettingsRetriever, cancellationToken))
            {
                requiresSave = true;
            }
        }

        if (requiresSave)
            await database.SaveChangesAsync(cancellationToken);
    }
}
