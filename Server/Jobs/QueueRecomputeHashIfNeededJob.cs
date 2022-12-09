namespace ThriveDevCenter.Server.Jobs;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Models;

public class QueueRecomputeHashIfNeededJob : IJob
{
    private readonly ApplicationDbContext database;

    public QueueRecomputeHashIfNeededJob(ApplicationDbContext database)
    {
        this.database = database;
    }

    public async Task Execute(CancellationToken cancellationToken)
    {
        bool needToRun = await database.Users.Where(u => u.LfsToken != null && u.HashedLfsToken == null)
            .AnyAsync(cancellationToken);

        if (needToRun)
        {
            BackgroundJob.Enqueue<RecomputeHashedColumns>(x => x.Execute(CancellationToken.None));
        }
    }
}
