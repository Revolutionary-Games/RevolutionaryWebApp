namespace ThriveDevCenter.Client.Shared
{
    using System.Threading;
    using System.Threading.Tasks;
    using ThriveDevCenter.Shared.Notifications;

    public interface INotificationHandler<in T>
        where T : SerializedNotification
    {
        Task Handle(T notification, CancellationToken cancellationToken);
    }
}
