namespace ThriveDevCenter.Server.Hubs;

using System.Text.Json;
using System.Threading.Tasks;
using Shared.Notifications;

public static class NotificationExtensions
{
    private static readonly NotificationJsonConverter Converter = new();

    /// <summary>
    ///   Send all SerializedNotification derived classes through this extension method
    /// </summary>
    public static Task ReceiveNotification(this INotifications receiver, SerializedNotification notification)
    {
        // TODO: unify with the startup code
        var serialized =
            JsonSerializer.Serialize(notification, new JsonSerializerOptions { Converters = { Converter } });

        return receiver.ReceiveNotificationJSON(serialized);
    }
}
