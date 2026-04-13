namespace RevolutionaryWebApp.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Services;

public class SetFinishedCIJobStatusJob : BaseCIJobManagingJob
{
    private readonly ILogger<SetFinishedCIJobStatusJob> logger;
    private readonly NotificationsEnabledDb database;

    public SetFinishedCIJobStatusJob(ILogger<SetFinishedCIJobStatusJob> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient, IGithubCommitStatusReporter statusReporter) : base(logger, database,
        jobClient, statusReporter)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(long ciProjectId, long ciBuildId, long ciJobId, bool success,
        CancellationToken cancellationToken)
    {
        var job = await database.CiJobs.Include(j => j.Build!).ThenInclude(b => b.CiProject)
            .FirstOrDefaultAsync(j => j.CiProjectId == ciProjectId && j.CiBuildId == ciBuildId && j.CiJobId == ciJobId,
                cancellationToken);

        if (job == null)
        {
            logger.LogError("Failed to get CI job to report final status on");
            return;
        }

        logger.LogInformation("CI job {CIProjectId}-{CIBuildId}-{CIJobId} is now finished with status: {Success}",
            ciProjectId, ciBuildId, ciJobId, success);

        job.SetFinishSuccess(success);

        // Send notifications about the job
        await OnJobEnded(job);
    }
}
