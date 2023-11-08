namespace ThriveDevCenter.Server.Jobs;

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;

public class CountPrecompiledObjectSizeJob
{
    private readonly ILogger<CountPrecompiledObjectSizeJob> logger;
    private readonly ApplicationDbContext database;

    public CountPrecompiledObjectSizeJob(ILogger<CountPrecompiledObjectSizeJob> logger, ApplicationDbContext database)
    {
        this.logger = logger;
        this.database = database;
    }

    public async Task Execute(long id, CancellationToken cancellationToken)
    {
        var precompiledVersion = await database.PrecompiledObjects.FindAsync(new object[] { id }, cancellationToken);

        if (precompiledVersion == null)
        {
            logger.LogError("Cannot count size of non-existent PrecompiledObject: {Id}", id);
            return;
        }

        long size = await database.PrecompiledObjectVersions.Where(v => v.OwnedById == precompiledVersion.Id)
            .SumAsync(v => v.Size, cancellationToken);

        precompiledVersion.TotalStorageSize = size;

        await database.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Size of PrecompiledObject {Id} is now {Size}", id, size);
    }
}
