namespace RevolutionaryWebApp.Server.Jobs.Maintenance;

using System;
using System.Threading;
using System.Threading.Tasks;
using Controllers;
using Hangfire;
using Microsoft.Extensions.Logging;
using Models;

/// <summary>
///   Base for all maintenance operations available through <see cref="MaintenanceController"/>
/// </summary>
[DisableConcurrentExecution(1500)]
public abstract class MaintenanceJobBase
{
    protected readonly ILogger<MaintenanceJobBase> logger;
    protected readonly ApplicationDbContext database;

    /// <summary>
    ///   Separate database that has notifications on for the status of the maintenance job itself, so that the actions
    ///   done by the maintenance operation don't unnecessarily send update notifications
    /// </summary>
    private readonly NotificationsEnabledDb operationStatusDb;

    public MaintenanceJobBase(ILogger<MaintenanceJobBase> logger, ApplicationDbContext operationDb,
        NotificationsEnabledDb operationStatusDb)
    {
        this.logger = logger;
        database = operationDb;
        this.operationStatusDb = operationStatusDb;
    }

    protected bool DummyOperation { get; private set; }

    public async Task Execute(long operationId, CancellationToken cancellationToken)
    {
        var operation = await operationStatusDb.ExecutedMaintenanceOperations.FindAsync(operationId);

        if (operation == null)
        {
            DummyOperation = true;
            logger.LogWarning("Maintenance operation {OperationId} not found in DB, using a dummy operation",
                operationId);
            operation = new ExecutedMaintenanceOperation("dummy");
        }

        var previousMessage = operation.ExtendedDescription;

        try
        {
            await RunOperation(operation, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            if (DummyOperation)
            {
                logger.LogError(e, "Maintenance job failed with a dummy operation");
            }
            else
            {
                logger.LogError(e, "Maintenance job failed, updating status in DB");

                operation.Failed = true;
                operation.ExtendedDescription = $"Failed with exception ({e.GetType().Name}): {e.Message}";

                await operationStatusDb.SaveChangesAsync(cancellationToken);
            }

            throw;
        }

        operation.Failed = false;
        operation.FinishedAt = DateTime.UtcNow;

        if (operation.ExtendedDescription == previousMessage)
            operation.ExtendedDescription = "Success";

        logger.LogInformation("Maintenance job {OperationId} succeeded", operationId);

        // Don't want to cancel after success
        // ReSharper disable once MethodSupportsCancellation
        await operationStatusDb.SaveChangesAsync();
    }

    protected abstract Task RunOperation(ExecutedMaintenanceOperation operationData,
        CancellationToken cancellationToken);
}
