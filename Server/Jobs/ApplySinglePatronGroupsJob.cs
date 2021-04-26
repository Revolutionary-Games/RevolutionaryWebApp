namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Models;

    public class ApplySinglePatronGroupsJob
    {
        private readonly ApplicationDbContext database;

        public ApplySinglePatronGroupsJob(ApplicationDbContext database)
        {
            this.database = database;
        }

        public async Task Execute(string email, CancellationToken cancellationToken)
        {
            // TODO: implement

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
