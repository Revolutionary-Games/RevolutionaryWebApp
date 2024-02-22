namespace RevolutionaryWebApp.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

[DisableConcurrentExecution(500)]
public class DeleteCiBuildJob
{
    private readonly ILogger<DeleteCiBuildJob> logger;
    private readonly ApplicationDbContext database;

    public DeleteCiBuildJob(ILogger<DeleteCiBuildJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(long ciProjectId, long ciBuildId, CancellationToken cancellationToken)
    {
        var build = await database.CiBuilds.Include(b => b.CiJobs).FirstOrDefaultAsync(
            b => b.CiProjectId == ciProjectId && b.CiBuildId == ciBuildId, cancellationToken);

        if (build == null)
        {
            logger.LogWarning("Can't delete CI build doesn't exist: {ProjectId}-{BuildId}", ciProjectId, ciBuildId);
            return;
        }

        logger.LogInformation("Deleting CI build from DB: {ProjectId}-{BuildId}", build.CiProjectId, build.CiBuildId);

        // Delete the contained jobs first
        if (build.CiJobs.Count < 1)
            logger.LogWarning("CI build to delete doesn't have any jobs in it to delete");

        CiJob.DeleteJobs(database, build.CiJobs);

        database.CiBuilds.Remove(build);
        await database.SaveChangesAsync(cancellationToken);
    }
}
