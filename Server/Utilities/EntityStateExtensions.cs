namespace RevolutionaryWebApp.Server.Utilities;

using System;
using Microsoft.EntityFrameworkCore;
using Shared.Notifications;

public static class EntityStateExtensions
{
    public static ListItemChangeType ToChangeType(this EntityState state)
    {
        switch (state)
        {
            case EntityState.Deleted:
                return ListItemChangeType.ItemDeleted;
            case EntityState.Modified:
                return ListItemChangeType.ItemUpdated;
            case EntityState.Added:
                return ListItemChangeType.ItemAdded;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }
    }
}
