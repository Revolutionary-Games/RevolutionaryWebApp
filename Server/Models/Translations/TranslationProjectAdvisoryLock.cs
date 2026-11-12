namespace RevolutionaryWebApp.Server.Models.Translations;

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Models;
using Services;

/// <summary>
///   A PostgreSQL advisory lock for serializing changes to one translation project across application instances.
/// </summary>
public sealed class TranslationProjectAdvisoryLock : IAsyncDisposable
{
    private const string LockKeyExpression =
        "hashtextextended('RevolutionaryWebApp:TranslationProject:' || CAST(@projectId AS text), 0)";

    private const string SyncLockKeyExpression =
        "hashtextextended('RevolutionaryWebApp:TranslationProjectSync:' || CAST(@projectId AS text), 0)";

    private readonly DbConnection? connection;
    private readonly long projectId;
    private readonly bool closeConnection;

    private IModelUpdateNotificationSender? notificationSender;

    private TranslationProjectAdvisoryLock(DbConnection? connection, long projectId, bool closeConnection)
    {
        this.connection = connection;
        this.projectId = projectId;
        this.closeConnection = closeConnection;
    }

    public static async Task<TranslationProjectAdvisoryLock> AcquireAsync(ApplicationDbContext database,
        long projectId, CancellationToken cancellationToken = default)
    {
        if (database.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            if (database.Database.IsRelational())
            {
                throw new InvalidOperationException("Translation project locks require the PostgreSQL provider");
            }

            return new TranslationProjectAdvisoryLock(null, projectId, false);
        }

        var connection = database.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await ExecuteLockCommand(connection, projectId, true, cancellationToken);
            return new TranslationProjectAdvisoryLock(connection, projectId, closeConnection);
        }
        catch
        {
            if (closeConnection)
                await connection.CloseAsync();

            throw;
        }
    }

    /// <summary>
    ///   Acquires a lock for a long-running sync and notifies project viewers on acquisition and release.
    /// </summary>
    public static async Task<TranslationProjectAdvisoryLock> AcquireForSyncAsync(ApplicationDbContext database,
        long projectId, IModelUpdateNotificationSender notificationSender,
        CancellationToken cancellationToken = default)
    {
        var projectLock = await AcquireAsync(database, projectId, cancellationToken);
        projectLock.notificationSender = notificationSender;
        try
        {
            if (projectLock.connection != null)
                await projectLock.ExecuteSyncLockCommand(true);

            await notificationSender.SendNotifications([TranslationNotificationHelper.ProjectUpdated(projectId)]);
            return projectLock;
        }
        catch
        {
            await projectLock.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    ///   Checks the live database lock without waiting for an operation to finish. PostgreSQL releases this
    ///   lock even if the worker process dies, so a failed worker cannot leave a stale busy flag.
    /// </summary>
    public static async Task<bool> IsHeldAsync(ApplicationDbContext database, long projectId,
        CancellationToken cancellationToken = default)
    {
        if (database.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            return false;

        var connection = database.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Query uses a const value input
            command.CommandText = $"""
                SELECT EXISTS (
                    SELECT 1 FROM pg_locks
                    WHERE locktype = 'advisory' AND granted AND objsubid = 1
                        AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                        AND classid = (({SyncLockKeyExpression} >> 32) & 4294967295)::oid
                        AND objid = ({SyncLockKeyExpression} & 4294967295)::oid
                )
                """;
#pragma warning restore CA2100

            var parameter = command.CreateParameter();
            parameter.ParameterName = "projectId";
            parameter.DbType = DbType.Int64;
            parameter.Value = projectId;

            command.Parameters.Add(parameter);
            return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (connection != null)
            {
                try
                {
                    try
                    {
                        if (notificationSender != null)
                            await ExecuteSyncLockCommand(false);
                    }
                    finally
                    {
                        await ExecuteLockCommand(connection, projectId, false, CancellationToken.None);
                    }
                }
                finally
                {
                    if (closeConnection)
                        await connection.CloseAsync();
                }
            }
        }
        finally
        {
            if (notificationSender != null)
                await notificationSender.SendNotifications([TranslationNotificationHelper.ProjectUpdated(projectId)]);
        }
    }

    private static async Task ExecuteLockCommand(DbConnection connection, long projectId, bool acquire,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 0;

#pragma warning disable CA2100 // Query uses a const value input
        command.CommandText = acquire ?
            $"SELECT pg_advisory_lock({LockKeyExpression})" :
            $"SELECT pg_advisory_unlock({LockKeyExpression})";
#pragma warning restore CA2100

        var projectIdParameter = command.CreateParameter();
        projectIdParameter.ParameterName = "projectId";
        projectIdParameter.DbType = DbType.Int64;
        projectIdParameter.Value = projectId;
        command.Parameters.Add(projectIdParameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private async Task ExecuteSyncLockCommand(bool acquire)
    {
        await using var command = connection!.CreateCommand();
#pragma warning disable CA2100 // Query uses a const value input
        command.CommandText = acquire ?
            $"SELECT pg_advisory_lock({SyncLockKeyExpression})" :
            $"SELECT pg_advisory_unlock({SyncLockKeyExpression})";
#pragma warning restore CA2100

        var parameter = command.CreateParameter();
        parameter.ParameterName = "projectId";
        parameter.DbType = DbType.Int64;
        parameter.Value = projectId;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(CancellationToken.None);
    }
}
