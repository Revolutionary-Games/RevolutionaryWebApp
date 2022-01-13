namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;

    public class DeleteDebugSymbolIfUploadFailedJob
    {
        private readonly ILogger<DeleteDebugSymbolIfUploadFailedJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public DeleteDebugSymbolIfUploadFailedJob(ILogger<DeleteDebugSymbolIfUploadFailedJob> logger,
            NotificationsEnabledDb database, IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        public static async Task<DebugSymbol> DeleteDebugSymbolFinal(long symbolId, ApplicationDbContext database,
            CancellationToken cancellationToken)
        {
            var symbol = await database.DebugSymbols.Include(s => s.StoredInItem)
                .ThenInclude(i => i.StorageItemVersions).Where(s => s.Id == symbolId)
                .FirstOrDefaultAsync(cancellationToken);

            if (symbol == null)
                return null;

            if (symbol.StoredInItem.StorageItemVersions.Count > 0)
                throw new Exception("Symbol's storage item still has existing versions");

            // These need to be deleted in this order to not cause a constraint error
            database.DebugSymbols.Remove(symbol);
            database.StorageItems.Remove(symbol.StoredInItem);

            return symbol;
        }

        public async Task Execute(long symbolId, CancellationToken cancellationToken)
        {
            var symbol = await database.DebugSymbols.Include(s => s.StoredInItem)
                .ThenInclude(i => i.StorageItemVersions).Where(s => s.Id == symbolId)
                .FirstOrDefaultAsync(cancellationToken);

            if (symbol == null)
            {
                logger.LogInformation("Not running delete failed upload symbol as symbol doesn't exist: {SymbolId}",
                    symbolId);
                return;
            }

            if (symbol.Uploaded)
            {
                logger.LogInformation("Symbol {Id} is uploaded", symbol.Id);
                return;
            }

            logger.LogWarning("Symbol {Id} has not been uploaded successfully, deleting it", symbol.Id);

            // Queue the jobs to perform the actions
            foreach (var storageItemVersion in symbol.StoredInItem.StorageItemVersions)
            {
                jobClient.Enqueue<DeleteStorageItemVersionJob>(x =>
                    x.Execute(storageItemVersion.Id, CancellationToken.None));
            }

            jobClient.Schedule<DeleteDebugSymbolIfUploadFailedJob>(
                x => x.PerformFinalDelete(symbolId, CancellationToken.None), TimeSpan.FromSeconds(90));

            // We can't finish anything here yet as the StorageItem needs to be deleted at the same time as the symbol
        }

        public async Task PerformFinalDelete(long symbolId, CancellationToken cancellationToken)
        {
            var symbol = await DeleteDebugSymbolFinal(symbolId, database, cancellationToken);

            if (symbol == null)
            {
                logger.LogError(
                    "Debug symbol disappeared before upload fail final delete task could run on: {SymbolId}",
                    symbolId);
                return;
            }

            logger.LogInformation("Performing final delete on symbol {Id} as it had not been uploaded successfully",
                symbol.Id);

            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"Deleted failed to be uploaded DebugSymbol {symbol.Id}",
            }, cancellationToken);

            await database.SaveChangesAsync(cancellationToken);
        }
    }
}
