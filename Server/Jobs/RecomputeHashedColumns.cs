namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Utilities;

    /// <summary>
    ///   Goes through all the models in the database that have hashed columns and computes the values for those
    ///   if missing. This loads a bunch of stuff at once in memory, so this won't work if there is a ton of data.
    ///   This is automatically triggered if it is detected on startup that this probably needs to run.
    /// </summary>
    public class RecomputeHashedColumns : IJob
    {
        private readonly ILogger<RecomputeHashedColumns> logger;
        private readonly ApplicationDbContext database;

        public RecomputeHashedColumns(ILogger<RecomputeHashedColumns> logger, ApplicationDbContext database)
        {
            this.logger = logger;
            this.database = database;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            logger.LogInformation("Starting recompute for hashed database columns");

            await ProcessSingleModel(database.Sessions, cancellationToken);
            await ProcessSingleModel(database.AccessKeys, cancellationToken);
            await ProcessSingleModel(database.LauncherLinks, cancellationToken);
            await ProcessSingleModel(database.RedeemableCodes, cancellationToken);
            await ProcessSingleModel(database.Users, cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            logger.LogInformation("Saving changes...");
            await database.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Done");
        }

        private async Task ProcessSingleModel<T>(DbSet<T> entriesInDb, CancellationToken cancellationToken)
        where T: class, IContainsHashedLookUps
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            // With careful programming and dynamic Expression building it would probably be possible to only find
            // entries that have missing fields. We go for simplicity here and just update them all
            var entities = await entriesInDb.ToListAsync(cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            foreach (var entity in entities)
            {
                entity.ComputeHashedLookUpValues();
            }
        }
    }
}
