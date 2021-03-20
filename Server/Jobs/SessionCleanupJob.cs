namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;
    using Models;
    using Quartz;

    [DisallowConcurrentExecution]
    public class SessionCleanupJob : IJob
    {
        private readonly ILogger<SessionCleanupJob> logger;
        private readonly ApplicationDbContext database;

        public SessionCleanupJob(ILogger<SessionCleanupJob> logger, ApplicationDbContext database)
        {
            this.logger = logger;
            this.database = database;
        }

        public Task Execute(IJobExecutionContext context)
        {
            logger.LogInformation("Starting database sessions cleanup");
            return Task.CompletedTask;
        }
    }
}
