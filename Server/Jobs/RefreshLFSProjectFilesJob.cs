namespace ThriveDevCenter.Server.Jobs;

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Models;
using Services;
using Utilities;

public class RefreshLFSProjectFilesJob
{
    private readonly ILogger<RefreshLFSProjectFilesJob> logger;
    private readonly NotificationsEnabledDb database;
    private readonly ILocalTempFileLocks localTempFileLocks;

    public RefreshLFSProjectFilesJob(ILogger<RefreshLFSProjectFilesJob> logger, NotificationsEnabledDb database,
        ILocalTempFileLocks localTempFileLocks)
    {
        this.logger = logger;
        this.database = database;
        this.localTempFileLocks = localTempFileLocks;
    }

    public async Task Execute(long id, CancellationToken cancellationToken)
    {
        var project = await database.LfsProjects.FindAsync(id);

        if (project == null)
        {
            logger.LogWarning("LFSProject {Id} not found, can't refresh files", id);
            return;
        }

        logger.LogDebug("Checking LFS file tree refresh for {Name}", project.Name);
        await LFSProjectTreeBuilder.BuildFileTree(localTempFileLocks, database, project, logger, cancellationToken);
    }
}