namespace RevolutionaryWebApp.Server.Models.Translations;

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Models;

/// <summary>
///   A PostgreSQL advisory lock for serializing changes to one translation project across application instances.
/// </summary>
public sealed class TranslationProjectAdvisoryLock : IAsyncDisposable
{
    private const string LockKeyExpression =
        "hashtextextended('RevolutionaryWebApp:TranslationProject:' || CAST(@projectId AS text), 0)";

    private readonly DbConnection? connection;
    private readonly long projectId;
    private readonly bool closeConnection;

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

    public async ValueTask DisposeAsync()
    {
        if (connection == null)
            return;

        try
        {
            await ExecuteLockCommand(connection, projectId, false, CancellationToken.None);
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
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
}
