namespace RevolutionaryWebApp.Client.Utilities;

using System;
using System.Collections.Generic;
using Models;

public class ClientSideResourceStatus<T> : ClientSideResourceStatus<T, long>
    where T : class, IDeletedResourceStatus, new()
{
}

public class ClientSideResourceStatus<T, TId>
    where T : class, IDeletedResourceStatus, new()
    where TId : notnull
{
    private readonly Dictionary<TId, T> statuses = new();

    public T GetStatus(TId resourceId)
    {
        lock (statuses)
        {
            statuses.TryAdd(resourceId, new T());

            return statuses[resourceId];
        }
    }

    public void SetDeletedStatus(TId resourceId)
    {
        GetStatus(resourceId).Deleted = true;
    }

    public bool IsDeleted(TId resourceId)
    {
        if (!statuses.TryGetValue(resourceId, out var status))
            return false;

        return status.Deleted;
    }

    public bool HasStatus(TId resourceId)
    {
        return statuses.ContainsKey(resourceId);
    }

    /// <summary>
    ///   Clears all deleted flags. Useful when creating new objects where the ID is not known but may conflict with
    ///   a previously deleted item, and it needs to be shown.
    /// </summary>
    public void ClearAllDeletedFlags()
    {
        foreach (var status in statuses.Values)
        {
            status.Deleted = false;
        }
    }

    public void ForEach(Action<T> operation)
    {
        foreach (var status in statuses.Values)
        {
            operation(status);
        }
    }
}
