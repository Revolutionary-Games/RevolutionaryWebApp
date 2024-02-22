namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Notifications;

/// <summary>
///   Helper for sending model update notifications
/// </summary>
public interface IModelUpdateNotificationSender
{
    public IEnumerable<Tuple<SerializedNotification, string>> OnChangesDetected(EntityState newState,
        IUpdateNotifications value, bool previousSoftDeleted);

    public Task SendNotifications(IEnumerable<Tuple<SerializedNotification, string>> notifications);
}

public class ModelUpdateNotificationSender : IModelUpdateNotificationSender
{
    private readonly IHubContext<NotificationsHub, INotifications> notifications;

    public ModelUpdateNotificationSender(IHubContext<NotificationsHub, INotifications> notifications)
    {
        this.notifications = notifications;
    }

    public IEnumerable<Tuple<SerializedNotification, string>> OnChangesDetected(EntityState newState,
        IUpdateNotifications value, bool previousSoftDeleted)
    {
        switch (newState)
        {
            case EntityState.Added:
            case EntityState.Deleted:
            case EntityState.Modified:
                break;
            default:
                throw new ArgumentException();
        }

        // Detect soft delete
        if (value.UsesSoftDelete && newState == EntityState.Modified)
        {
            if (value.IsSoftDeleted != previousSoftDeleted)
            {
                // State changed
                newState = previousSoftDeleted ? EntityState.Added : EntityState.Deleted;
            }
        }

        return value.GetNotifications(newState);
    }

    public Task SendNotifications(IEnumerable<Tuple<SerializedNotification, string>> toSend)
    {
        var tasks = new List<Task> { Capacity = 10 };

        foreach (var tuple in toSend)
        {
            tasks.Add(notifications.Clients.Group(tuple.Item2).ReceiveNotification(tuple.Item1));
        }

        return Task.WhenAll(tasks);
    }
}
