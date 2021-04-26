namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Models;

    public class CheckSSOUserSuspensionJob
    {
        private readonly ApplicationDbContext database;

        public CheckSSOUserSuspensionJob(ApplicationDbContext database)
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
