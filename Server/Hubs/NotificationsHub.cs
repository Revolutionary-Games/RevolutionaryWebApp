namespace ThriveDevCenter.Server.Hubs
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;

    public class NotificationsHub : Hub<INotifications>
    {
        private readonly JwtTokens csrfVerifier;
        private User connectedUser;

        public NotificationsHub(JwtTokens csrfVerifier)
        {
            this.csrfVerifier = csrfVerifier;
        }

        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();

            if (http != null)
            {
                var queryParams = http.Request.Query;

                if (!queryParams.TryGetValue("minorVersion", out StringValues minorStr) ||
                    !queryParams.TryGetValue("majorVersion", out StringValues majorStr) ||
                    !queryParams.TryGetValue("access_token", out StringValues accessToken))
                {
                    throw new HubException("invalid connection parameters");
                }

                if (minorStr.Count < 1 || majorStr.Count < 1 || accessToken.Count < 1)
                    throw new HubException("invalid connection parameters");

                if (!csrfVerifier.IsValidCSRFToken(accessToken[0]))
                    throw new HubException("invalid csrf token");

                bool invalidVersion = false;

                try
                {
                    var major = Convert.ToInt32(majorStr[0]);
                    var minor = Convert.ToInt32(minorStr[0]);

                    if (major != AppInfo.Major || minor != AppInfo.Minor)
                        invalidVersion = true;
                }
                catch (Exception)
                {
                    throw new HubException("invalid connection parameters");
                }

                if (invalidVersion)
                    await Clients.Caller.ReceiveVersionMismatch();

                if(http.Request.Cookies.TryGetValue(AppInfo.SessionCookieName, out string session)){
                    // TODO: handle user detection
                    connectedUser = null;
                }
            }

            // TODO: remove this test delay
            await Task.Delay(TimeSpan.FromMilliseconds(950));

            await Clients.Caller.ReceiveOwnUserInfo(connectedUser?.GetInfo(RecordAccessLevel.Private));

            await base.OnConnectedAsync();

            // Could send some user specific notices here
            // await Clients.Caller.ReceiveSiteNotice(SiteNoticeType.Primary, "hey you connected");
        }

        public Task JoinGroup(string groupName)
        {
            // TODO: This needs authentication to ensure protected groups can't be joined
            return Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public Task LeaveGroup(string groupName)
        {
            // TODO: does this need also group checking?
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        public Task SendSiteNotice(SiteNoticeType type, string message)
        {
            // TODO: needs to be restricted to admin
            return Clients.All.ReceiveSiteNotice(type, message);
        }

        public Task WhoAmI()
        {
            // TODO: reload from Db at some interval
            return Clients.Caller.ReceiveOwnUserInfo(connectedUser?.GetInfo(RecordAccessLevel.Private));
        }
    }

    public interface INotifications
    {
        // These are general connection related things sent
        Task ReceiveSiteNotice(SiteNoticeType type, string message);
        Task ReceiveSessionInvalidation();
        Task ReceiveVersionMismatch();
        Task ReceiveOwnUserInfo(UserInfo user);

        // Directly sending SerializedNotification doesn't work so we hack around that by manually serializing it
        // to a string before sending
        Task ReceiveNotificationJSON(string json);
    }

    public static class NotificationHelpers
    {
        private static readonly NotificationJsonConverter Converter = new NotificationJsonConverter();

        /// <summary>
        ///   Send all SerializedNotification derived classes through this extension method
        /// </summary>
        public static Task ReceiveNotification(this INotifications receiver, SerializedNotification notification)
        {
            var serialized =
                JsonSerializer.Serialize(notification, new JsonSerializerOptions() { Converters = { Converter } });

            return receiver.ReceiveNotificationJSON(serialized);
        }
    }
}
