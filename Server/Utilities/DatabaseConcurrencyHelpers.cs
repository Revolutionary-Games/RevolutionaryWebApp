namespace RevolutionaryWebApp.Server.Utilities;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
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
    ///     After running this method any necessary modifications to the <see cref="entry"/> need to be redone
    ///   </para>
    /// </remarks>
    /// <param name="conflicts">
    ///   The conflicts to resolve. For example from <see cref="SaveChangesWithConflictResolvingAsync"/>
    /// </param>
    /// <param name="entry">The entry to update with DB values</param>
    /// <param name="treatUnchanged">
    ///   If true, then the conflict entities are marked as non-modified after this call finishes. Maybe required
    ///   explicitly when navigations are loaded and need to be cleared (or foreign keys are adjusted).
    /// </param>
    /// <param name="clearAllNavigations">
    ///   If true, all loaded navigations of the conflict object are cleared, when false only conflicting ones are
    ///   unloaded.
    /// </param>
    public static void ResolveSingleEntityConcurrencyConflict(IReadOnlyList<EntityEntry> conflicts, object entry,
        bool treatUnchanged = false, bool clearAllNavigations = false)
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

            // Replace all values with the new values
            foreach (var property in proposedValues.Properties)
            {
                proposedValues[property] = databaseValues[property];
            }

            // This updates the original copy loaded from the DB to mark the conflict as resolved
            conflictEntry.OriginalValues.SetValues(databaseValues);

            // Clear navigation-side relationship state that may keep FK modifications alive
            if (clearAllNavigations)
            {
                foreach (var navigation in conflictEntry.Navigations)
                {
                    if (navigation.Metadata.IsCollection)
                    {
                        // Collections usually need to be reloaded or reassigned explicitly by the caller
                        continue;
                    }

                    navigation.CurrentValue = null;
                }

                foreach (var reference in conflictEntry.References)
                {
                    reference.TargetEntry?.State = EntityState.Detached;
                }
            }
            else
            {
                // TODO: this code is kind of unverified, as it seems "treatUnchanged" is the magic that made runner
                // connection handlers work to clear the old data
                foreach (var foreignKey in conflictEntry.Metadata.GetForeignKeys())
                {
                    var fkChanged = foreignKey.Properties.Any(p =>
                        !Equals(conflictEntry.Property(p.Name).CurrentValue,
                            conflictEntry.Property(p.Name).OriginalValue));

                    if (!fkChanged)
                        continue;

                    foreach (var navigation in foreignKey.DependentToPrincipal != null ?
                                 new[] { foreignKey.DependentToPrincipal } :
                                 Enumerable.Empty<INavigation>())
                    {
                        conflictEntry.Navigation(navigation.Name).CurrentValue = null;
                    }
                }
            }

            if (treatUnchanged)
            {
                conflictEntry.State = EntityState.Unchanged;
            }
        }
    }
}
