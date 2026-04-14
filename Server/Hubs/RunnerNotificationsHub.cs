namespace RevolutionaryWebApp.Server.Hubs;

using System;
using System.Linq;
using System.Threading.Tasks;
using Common.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Models;
using Utilities;

/// <summary>
///   <see cref="RemoteRunner"/> specific hub for notices.
/// </summary>
public class RunnerNotificationsHub : Hub<IRunnerNotifications>
{
    private readonly ILogger<RunnerNotificationsHub> logger;
    private readonly ApplicationDbContext database;

    public RunnerNotificationsHub(ILogger<RunnerNotificationsHub> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public override async Task OnConnectedAsync()
    {
        var http = Context.GetHttpContext();

        // Verify that the runner uses its identity key to receive notices
        if (http != null)
        {
            var queryParams = http.Request.Query;

            if (!queryParams.TryGetValue("key", out StringValues key))
            {
                throw new HubException("invalid connection parameters");
            }

            if (key.Count < 1)
                throw new HubException("invalid connection parameters");

            var runnerAccessKey = key[0];

            if (string.IsNullOrWhiteSpace(runnerAccessKey))
                throw new HubException("invalid connection parameters");

            if (!Guid.TryParse(runnerAccessKey, out var parsedKey))
                throw new HubException("invalid key format");

            var runner = await database.RemoteRunners.WhereHashed(nameof(RemoteRunner.AccessId), parsedKey.ToString())
                .AsAsyncEnumerable().FirstOrDefaultAsync(r => r.AccessId == parsedKey);

            if (runner == null)
            {
                throw new HubException("invalid key");
            }

            Context.Items["Runner"] = runner;
        }
        else
        {
            logger.LogWarning("No HTTP context available");
            throw new HubException("invalid connection parameters");
        }
    }
}
