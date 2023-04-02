namespace ThriveDevCenter.Server.Hubs;

using System.Threading.Tasks;
using Shared;
using Shared.Models;

public interface INotifications
{
    // These are general connection related things sent
    public Task ReceiveSiteNotice(SiteNoticeType type, string message);
    public Task ReceiveSessionInvalidation();
    public Task ReceiveVersionMismatch();
    public Task ReceiveOwnUserInfo(UserDTO? user);

    // Directly sending SerializedNotification doesn't work so we hack around that by manually serializing it
    // to a string before sending
    public Task ReceiveNotificationJSON(string json);
}
