namespace ThriveDevCenter.Server.Services
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Models;

    /// <summary>
    ///   Helper for sending model update notifications
    /// </summary>
    public class ModelUpdateNotificationSender : IModelUpdateNotificationSender
    {
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public ModelUpdateNotificationSender(IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.notifications = notifications;
        }

        public Task OnChangesDetected(EntityState newState, IUpdateNotifications value, bool previousSoftDeleted)
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
            if (newState == EntityState.Modified)
            {
                if (value.IsSoftDeleted != previousSoftDeleted)
                {
                    // State changed
                    newState = previousSoftDeleted ? EntityState.Added : EntityState.Deleted;
                }
            }

            return Task.WhenAll(value.GetNotifications(newState).Select(tuple =>
                notifications.Clients.Group(tuple.Item2).ReceiveNotification(tuple.Item1)));
        }
    }

    public interface IModelUpdateNotificationSender
    {
        public Task OnChangesDetected(EntityState newState, IUpdateNotifications value, bool previousSoftDeleted);
    }
}
