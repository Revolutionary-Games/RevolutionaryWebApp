namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Shared;

public static class DatabaseConcurrencyHelpers
{
    public static async Task SaveChangesWithConflictResolvingAsync(this DbContext database,
        Action<IReadOnlyList<EntityEntry>> handleConflicts, CancellationToken cancellationToken,
        bool waitBetweenAttempts = true)
    {
        var random = new Random();

        int attempt = 0;

        while (true)
        {
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException e)
            {
                if (++attempt > AppInfo.DefaultDatabaseUpdateFailureAttempts)
                    throw;

                if (waitBetweenAttempts)
                    await Task.Delay(random.Next(1, 1000), cancellationToken);
                handleConflicts(e.Entries);
            }
        }
    }

    /// <summary>
    ///   Resolves single entity update conflict by replacing all values with new ones from the database
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     After running this method any needed modifications to the <see cref="entry"/> need to be redone
    ///   </para>
    /// </remarks>
    /// <param name="conflicts">
    ///   The conflicts to resolve. For example from <see cref="SaveChangesWithConflictResolvingAsync"/>
    /// </param>
    /// <param name="entry">The entry to update with DB values</param>
    public static void ResolveSingleEntityConcurrencyConflict(IReadOnlyList<EntityEntry> conflicts, object entry)
    {
        foreach (var conflictEntry in conflicts)
        {
            if (conflictEntry.Entity != entry)
            {
                throw new NotSupportedException(
                    $"Can't handle concurrency conflict for entity of type {conflictEntry.Metadata.Name}");
            }

            var proposedValues = conflictEntry.CurrentValues;
            var databaseValues = conflictEntry.GetDatabaseValues();

            if (databaseValues == null)
                throw new Exception("Original database values are null, can't apply them");

            foreach (var property in proposedValues.Properties)
            {
                // var proposedValue = proposedValues[property];
                var databaseValue = databaseValues[property];

                // Replace all values with the new values
                proposedValues[property] = databaseValue;
            }

            // This updates the original copy loaded from the DB to mark the conflict as resolved
            conflictEntry.OriginalValues.SetValues(databaseValues);
        }
    }
}
