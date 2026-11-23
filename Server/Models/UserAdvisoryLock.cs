namespace RevolutionaryWebApp.Server.Models;

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

/// <summary>
///   A PostgreSQL advisory lock for serializing operations on one user across application instances.
/// </summary>
public sealed class UserAdvisoryLock : IAsyncDisposable
{
    private const string LockKeyExpression =
        "hashtextextended('RevolutionaryWebApp:User:' || CAST(@userId AS text), 0)";

    private readonly DbConnection? connection;
    private readonly long userId;
    private readonly bool closeConnection;

    private UserAdvisoryLock(DbConnection? connection, long userId, bool closeConnection)
    {
        this.connection = connection;
        this.userId = userId;
        this.closeConnection = closeConnection;
    }

    public static async Task<UserAdvisoryLock> AcquireAsync(ApplicationDbContext database, long userId,
        CancellationToken cancellationToken = default)
    {
        if (database.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            if (database.Database.IsRelational())
                throw new InvalidOperationException("User advisory locks require the PostgreSQL provider");

            return new UserAdvisoryLock(null, userId, false);
        }

        var connection = database.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
            await connection.OpenAsync(cancellationToken);

        try
        {
            await ExecuteLockCommand(connection, userId, true, cancellationToken);
            return new UserAdvisoryLock(connection, userId, closeConnection);
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
            await ExecuteLockCommand(connection, userId, false, CancellationToken.None);
        }
        finally
        {
            if (closeConnection)
                await connection.CloseAsync();
        }
    }

    private static async Task ExecuteLockCommand(DbConnection connection, long userId, bool acquire,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 0;
        if (acquire)
        {
            command.CommandText = "SELECT pg_advisory_lock(" + LockKeyExpression + ")";
        }
        else
        {
            command.CommandText = "SELECT pg_advisory_unlock(" + LockKeyExpression + ")";
        }

        var parameter = command.CreateParameter();
        parameter.ParameterName = "userId";
        parameter.DbType = DbType.Int64;
        parameter.Value = userId;
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
