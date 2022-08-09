namespace ThriveDevCenter.Server.Jobs;

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
using Utilities;

[DisableConcurrentExecution(500)]
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

        var symbols = await database.DebugSymbols.Where(s => !s.Active && s.UpdatedAt < cutoff)
            .Include(s => s.StoredInItem!).ThenInclude(i => i.StorageItemVersions).ThenInclude(v => v.StorageFile)
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

            logger.LogInformation("Deleting old inactive symbol: {RelativePath} ({Id})", symbol.RelativePath,
                symbol.Id);

            if (symbol.StoredInItem == null)
                throw new NotLoadedModelNavigationException();

            // Queue the jobs to perform the actions
            foreach (var storageItemVersion in symbol.StoredInItem.StorageItemVersions)
            {
                jobClient.Enqueue<DeleteStorageItemVersionJob>(x =>
                    x.Execute(storageItemVersion.Id, CancellationToken.None));
            }

            jobClient.Schedule<DeleteOldDisabledSymbolsJob>(
                x => x.PerformFinalDelete(symbol.Id, CancellationToken.None), TimeSpan.FromSeconds(90));
        }
    }

    public async Task PerformFinalDelete(long symbolId, CancellationToken cancellationToken)
    {
        var symbol =
            await DeleteDebugSymbolIfUploadFailedJob.DeleteDebugSymbolFinal(symbolId, database, cancellationToken);

        if (symbol == null)
        {
            logger.LogError(
                "Debug symbol disappeared before old symbol delete task could run on: {SymbolId}",
                symbolId);
            return;
        }

        logger.LogInformation("Performing final delete on symbol {Id} as it is being deleted as old",
            symbol.Id);

        await database.LogEntries.AddAsync(new LogEntry()
        {
            Message = $"Deleted inactive symbol ({symbol.Id}): {symbol.RelativePath}",
        }, cancellationToken);

        await database.SaveChangesAsync(cancellationToken);
    }
}