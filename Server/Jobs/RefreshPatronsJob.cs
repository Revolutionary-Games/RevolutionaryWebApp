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

/// <summary>
///   Refreshes all patron info and queues the group apply job
/// </summary>
[DisableConcurrentExecution(1200)]
public class RefreshPatronsJob(ILogger<RefreshPatronsJob> logger, NotificationsEnabledDb database,
    IBackgroundJobClient jobClient)
    : IJob
{
    public async Task Execute(CancellationToken cancellationToken)
    {
        // TODO: if we ever have more patrons (unlikely) than can be kept in memory, this needs a different
        // approach

        var patrons = await database.Patrons.ToListAsync(cancellationToken);

        patrons.ForEach(p => p.Marked = false);

        foreach (var settings in await database.PatreonSettings.ToListAsync(cancellationToken))
        {
            if (settings.Active == false)
                continue;

            using var api = new PatreonCreatorAPI(settings);

            foreach (var actualPatron in await api.GetPatrons(settings, cancellationToken))
            {
                await PatreonGroupHandler.HandlePatreonPledgeObject(actualPatron.Pledge,
                    actualPatron.User, actualPatron.Reward?.Id, database, jobClient);

                if (cancellationToken.IsCancellationRequested)
                    throw new TaskCanceledException();
            }

            settings.LastRefreshed = DateTime.UtcNow;
        }

        foreach (var toDelete in patrons.Where(p => p.Marked == false))
        {
            await database.LogEntries.AddAsync(new LogEntry(
                    $"Destroying patron ({toDelete.Id}) because it is unmarked " +
                    "(wasn't found from fresh data from Patreon)"),
                cancellationToken);

            logger.LogInformation("Deleted unmarked Patron {Id}", toDelete.Id);
            database.Patrons.Remove(toDelete);

            // The webhook notification should already queue a user group check, this whole refresh thing is just
            // a safety measure in case we miss a webhook, so it is not absolutely necessary to queue a check here.
            // There is CheckAllUserAutomaticGroups as a fallback as well.
        }

        await database.SaveChangesAsync(cancellationToken);

        jobClient.Enqueue<ApplyPatronForumGroupsJob>(x => x.Execute(CancellationToken.None));
    }
}
