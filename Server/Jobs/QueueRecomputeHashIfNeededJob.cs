namespace ThriveDevCenter.Server.Jobs
{
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Models;

    public class QueueRecomputeHashIfNeededJob :IJob
    {
        private readonly ApplicationDbContext database;

        public QueueRecomputeHashIfNeededJob(ApplicationDbContext database)
        {
            this.database = database;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            // TODO: fix this once all the hashed columns have been added
            // bool needToRun = await database.Users.AsQueryable().Where(u => u.LfsToken != null && u.HashedLfsToken == null).AnyAsync();

            bool needToRun = true;
            if (needToRun)
            {
                BackgroundJob.Enqueue<RecomputeHashedColumns>(x => x.Execute(CancellationToken.None));
            }
        }
    }
}
