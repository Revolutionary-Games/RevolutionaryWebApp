namespace ThriveDevCenter.Server.Jobs
{
    using System;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Hangfire;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;

    public class DeleteOldDisabledSymbolsJob : IJob
    {
        private readonly ILogger<DeleteOldDisabledSymbolsJob> logger;
        private readonly NotificationsEnabledDb database;
        private readonly IBackgroundJobClient jobClient;

        public DeleteOldDisabledSymbolsJob(ILogger<DeleteOldDisabledSymbolsJob> logger, NotificationsEnabledDb database,
            IBackgroundJobClient jobClient)
        {
            this.logger = logger;
            this.database = database;
            this.jobClient = jobClient;
        }

        [SuppressMessage("ReSharper", "MethodSupportsCancellation",
            Justification = "deletes remote files so state is important to save for partial work")]
        public async Task Execute(CancellationToken cancellationToken)
        {
            var cutoff = DateTime.UtcNow - AppInfo.InactiveSymbolKeepDuration;

            var symbols = await database.DebugSymbols.AsQueryable().Where(s => !s.Active && s.UpdatedAt < cutoff)
                .Include(s => s.StoredInItem).ThenInclude(i => i.StorageItemVersions).ThenInclude(v => v.StorageFile)
                .ToListAsync(cancellationToken);

            if (symbols.Count < 1)
            {
                logger.LogInformation("No old debug symbols found");
                return;
            }

            foreach (var symbol in symbols)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await database.LogEntries.AddAsync(new LogEntry()
                {
                    Message = $"Deleted inactive symbol ({symbol.Id}): {symbol.RelativePath}",
                });

                logger.LogInformation("Deleting old inactive symbol: {RelativePath}", symbol.RelativePath);

                logger.LogInformation("Deleting old inactive symbol storage item {Id} and associated versions",
                    symbol.StoredInItem.Id);
                DeleteStorageItemJob.PerformProperDelete(symbol.StoredInItem, jobClient);

                database.DebugSymbols.Remove(symbol);
            }

            await database.SaveChangesAsync();
        }
    }
}
