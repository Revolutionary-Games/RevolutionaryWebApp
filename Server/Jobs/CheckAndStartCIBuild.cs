namespace ThriveDevCenter.Server.Jobs
{
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.Extensions.Logging;
    using Models;
    using Utilities;

    public class CheckAndStartCIBuild
    {
        private readonly ILogger<CheckAndStartCIBuild> logger;
        private readonly NotificationsEnabledDb database;

        public CheckAndStartCIBuild(ILogger<CheckAndStartCIBuild> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
        }

        public async Task Execute(long ciProjectId, long ciBuildId, CancellationToken cancellationToken)
        {
            var build = await database.CiBuilds.FindAsync(ciProjectId, ciBuildId);

            if (build == null)
            {
                logger.LogError("Failed to find CIBuild to start");
                return;
            }


        }
    }
}
