namespace ThriveDevCenter.Server.Jobs
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Models;

    /// <summary>
    ///   Scheduled job to queue refreshes for all non-deleted lfs project's file trees
    /// </summary>
    public class RefreshLFSProjectFileTreesJob : IJob
    {
        private readonly ApplicationDbContext database;
        private readonly IBackgroundJobClient jobClient;

        public RefreshLFSProjectFileTreesJob(ApplicationDbContext database, IBackgroundJobClient jobClient)
        {
            this.database = database;
            this.jobClient = jobClient;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            foreach (var id in await database.LfsProjects.AsQueryable().Where(p => p.Deleted != true).Select(p => p.Id)
                .ToListAsync(cancellationToken))
            {
                jobClient.Enqueue<RefreshLFSProjectFilesJob>(x => x.Execute(id, CancellationToken.None));
            }
        }
    }
}
