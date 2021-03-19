namespace ThriveDevCenter.Server.Hubs
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;

    public class NotificationsHub : Hub<INotifications>
    {
        private readonly JwtTokens csrfVerifier;
        private readonly ApplicationDbContext database;

        private User connectedUser;

        public NotificationsHub(JwtTokens csrfVerifier, ApplicationDbContext database)
        {
            this.csrfVerifier = csrfVerifier;
            this.database = database;
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

                try
                {
                    connectedUser =
                        await http.Request.Cookies.GetUserFromSession(database, http.Connection.RemoteIpAddress);
                }
                catch (ArgumentException)
                {
                    throw new HubException("invalid session cookie");
                }

                if (!csrfVerifier.IsValidCSRFToken(accessToken[0], connectedUser))
                    throw new HubException("invalid CSRF token");

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
            }

            await Clients.Caller.ReceiveOwnUserInfo(connectedUser?.GetInfo(RecordAccessLevel.Private));

            await base.OnConnectedAsync();

            // Could send some user specific notices here
            // await Clients.Caller.ReceiveSiteNotice(SiteNoticeType.Primary, "hey you connected");
        }

        public Task JoinGroup(string groupName)
        {
            if (!IsUserAllowedInGroup(groupName, connectedUser))
                throw new HubException("You don't have access to the specified group");

            return Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        }

        public Task LeaveGroup(string groupName)
        {
            // TODO: does this need also group checking?
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        public Task SendSiteNotice(SiteNoticeType type, string message)
        {
            // Only admins can send
            if (connectedUser == null || !connectedUser.HasAccessLevel(UserAccessLevel.Admin))
                throw new HubException("You don't have permission to perform this operation");

            return Clients.All.ReceiveSiteNotice(type, message);
        }

        public Task WhoAmI()
        {
            // TODO: reload from Db at some interval
            return Clients.Caller.ReceiveOwnUserInfo(connectedUser?.GetInfo(RecordAccessLevel.Private));
        }

        private static bool IsUserAllowedInGroup(string groupName, User user)
        {
            // First check explicitly named groups
            switch (groupName)
            {
                case NotificationGroups.UserListUpdated:
                    return RequireAccessLevel(UserAccessLevel.Admin, user);
                case NotificationGroups.LFSListUpdated:
                    return RequireAccessLevel(UserAccessLevel.NotLoggedIn, user);
                case NotificationGroups.PrivateLFSUpdated:
                    return RequireAccessLevel(UserAccessLevel.Developer, user);
            }

            // Then check prefixes

            // Unknown groups are not allowed
            return false;
        }

        private static bool RequireAccessLevel(UserAccessLevel level, User user)
        {
            // All users have the not logged in access level
            if (level == UserAccessLevel.NotLoggedIn)
                return true;

            // All other access levels require a user
            if (user == null)
                return false;

            return user.HasAccessLevel(level);
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
