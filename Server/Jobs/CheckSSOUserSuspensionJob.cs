namespace RevolutionaryWebApp.Server.Jobs;

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

[DisableConcurrentExecution(300)]
public class CheckSSOUserSuspensionJob
{
    private readonly ILogger<CheckSSOUserSuspensionJob> logger;
    private readonly ApplicationDbContext database;
    private readonly ICommunityForumAPI communityAPI;
    private readonly IDevForumAPI devForumAPI;

    public CheckSSOUserSuspensionJob(ILogger<CheckSSOUserSuspensionJob> logger, ApplicationDbContext database,
        ICommunityForumAPI communityAPI, IDevForumAPI devForumAPI)
    {
        this.logger = logger;
        this.database = database;
        this.communityAPI = communityAPI;
        this.devForumAPI = devForumAPI;
    }

    public async Task Execute(string email, CancellationToken cancellationToken)
    {
        var user = await database.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user == null)
        {
            logger.LogInformation("User to check SSO suspend status for doesn't exist, skipping job");
            return;
        }

        var patreonSettingsRetriever = new Lazy<Task<PatreonSettings>>(() =>
            database.PatreonSettings.OrderBy(s => s.Id).FirstAsync(cancellationToken));

        if (await SSOSuspendHandler.CheckUser(user, database, communityAPI, devForumAPI, logger,
                patreonSettingsRetriever, cancellationToken))
        {
            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
