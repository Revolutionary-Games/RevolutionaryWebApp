namespace RevolutionaryWebApp.Server.Jobs;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;

[DisableConcurrentExecution(500)]
public class RefreshLFSObjectStatisticsJob : IJob
{
    private readonly NotificationsEnabledDb database;

    public RefreshLFSObjectStatisticsJob(NotificationsEnabledDb database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        // Let's hope we never have so many projects that this isn't good enough
        var projectsToProcess = await database.LfsProjects.Where(p => p.Deleted != true).ToListAsync(cancellationToken);

        foreach (var project in projectsToProcess)
        {
            project.TotalObjectSize =
                await database.LfsObjects.Where(o => o.LfsProjectId == project.Id)
                    .SumAsync(o => o.Size, cancellationToken: cancellationToken);
            project.TotalObjectCount =
                await database.LfsObjects.CountAsync(o => o.LfsProjectId == project.Id, cancellationToken);

            project.TotalSizeUpdated = DateTime.UtcNow;
        }

        await database.SaveChangesAsync(cancellationToken);
    }
}
