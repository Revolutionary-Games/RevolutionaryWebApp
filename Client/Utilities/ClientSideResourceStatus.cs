namespace ThriveDevCenter.Client.Utilities;

using System.Collections.Generic;
using Models;

public class ClientSideResourceStatus<T>
    where T : class, IDeletedResourceStatus, new()
{
    private readonly Dictionary<long, T> statuses = new();

    public T GetStatus(long resourceId)
    {
        lock (statuses)
        {
            if (!statuses.ContainsKey(resourceId))
                statuses[resourceId] = new T();

            return statuses[resourceId];
        }
    }

    public void SetDeletedStatus(long resourceId)
    {
        GetStatus(resourceId).Deleted = true;
    }

    public bool IsDeleted(long resourceId)
    {
        if (!statuses.ContainsKey(resourceId))
            return false;

        return statuses[resourceId].Deleted;
    }

    public bool HasStatus(long resourceId)
    {
        return statuses.ContainsKey(resourceId);
    }

    /// <summary>
    ///   Clears all deleted flags. Useful when creating new objects where the ID is not known but may conflict with
    ///   a previously deleted item and it needs to be shown.
    /// </summary>
    public void ClearAllDeletedFlags()
    {
        foreach (var status in statuses.Values)
        {
            status.Deleted = false;
        }
    }
}
