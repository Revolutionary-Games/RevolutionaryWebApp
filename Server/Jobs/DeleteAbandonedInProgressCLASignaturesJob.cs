namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;

    public class DeleteAbandonedInProgressCLASignaturesJob : IJob
    {
        private readonly ILogger<DeleteAbandonedInProgressCLASignaturesJob> logger;
        private readonly ApplicationDbContext database;

        public DeleteAbandonedInProgressCLASignaturesJob(ILogger<DeleteAbandonedInProgressCLASignaturesJob> logger,
            ApplicationDbContext database)
        {
            this.logger = logger;
            this.database = database;
        }

        public async Task Execute(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow - AppInfo.DeleteAbandonedInProgressSignaturesAfter;

            var items = await database.InProgressClaSignatures.Where(i => i.UpdatedAt < cutoff)
                .ToListAsync(cancellationToken);

            if (items.Count < 1)
                return;

            database.InProgressClaSignatures.RemoveRange(items);

            await database.SaveChangesAsync(cancellationToken);

            foreach (var item in items)
            {
                logger.LogInformation("Deleted abandoned CLA signature: {Id}", item.Id);
            }
        }
    }
}
