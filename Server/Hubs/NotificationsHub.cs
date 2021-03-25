namespace ThriveDevCenter.Server.Hubs
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.Primitives;
    using Models;
    using Services;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;

    public class NotificationsHub : Hub<INotifications>
    {
        private readonly JwtTokens csrfVerifier;
        private readonly ApplicationDbContext database;

        public NotificationsHub(JwtTokens csrfVerifier, ApplicationDbContext database)
        {
            this.csrfVerifier = csrfVerifier;
            this.database = database;
        }

        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();
            User connectedUser = null;

            if (http != null)
            {
                var queryParams = http.Request.Query;

                if (!queryParams.TryGetValue("minorVersion", out StringValues minorStr) ||
                    !queryParams.TryGetValue("majorVersion", out StringValues majorStr))
                {
                    throw new HubException("invalid connection parameters");
                }

                if (minorStr.Count < 1 || majorStr.Count < 1)
                    throw new HubException("invalid connection parameters");

                string csrf;

                if (!queryParams.TryGetValue("access_token", out StringValues accessToken))
                {
                    // In release mode (at least I saw this happen once) the access token is in a header
                    if (http.Request.Headers.TryGetValue("Authorization", out StringValues header) &&
                        header.Count > 0 && header[0].StartsWith("Bearer "))
                    {
                        // In format "Bearer TOKEN"
                        csrf = header[0].Split(' ').Last();
                    }
                    else
                    {
                        throw new HubException("invalid connection parameters");
                    }
                }
                else
                {
                    if (accessToken.Count < 1)
                        throw new HubException("invalid connection parameters");

                    csrf = accessToken[0];
                }

                try
                {
                    connectedUser =
                        await http.Request.Cookies.GetUserFromSession(database, http.Connection.RemoteIpAddress);
                }
                catch (ArgumentException)
                {
                    throw new HubException("invalid session cookie");
                }

                if (!csrfVerifier.IsValidCSRFToken(csrf, connectedUser))
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

            Context.Items["User"] = connectedUser;

            await base.OnConnectedAsync();

            if (connectedUser == null)
            {
                await Clients.Caller.ReceiveOwnUserInfo(null);
            }
            else
            {
                await Clients.Caller.ReceiveOwnUserInfo(connectedUser.GetInfo(
                    connectedUser.HasAccessLevel(UserAccessLevel.Admin) ?
                        RecordAccessLevel.Admin :
                        RecordAccessLevel.Private));

                // Could send some user specific notices here
                // await Clients.Caller.ReceiveSiteNotice(SiteNoticeType.Primary, "hey you connected");
            }
        }

        public Task JoinGroup(string groupName)
        {
            if (!IsUserAllowedInGroup(groupName, Context.Items["User"] as User))
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
            if (!RequireAccessLevel(UserAccessLevel.Admin, Context.Items["User"] as User))
                throw new HubException("You don't have permission to perform this operation");

            return Clients.All.ReceiveSiteNotice(type, message);
        }

        public Task WhoAmI()
        {
            // TODO: reload from Db at some interval
            return Clients.Caller.ReceiveOwnUserInfo(
                (Context.Items["User"] as User)?.GetInfo(RecordAccessLevel.Private));
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
            if (groupName.StartsWith(NotificationGroups.UserUpdatedPrefix))
            {
                // Admins can see all user info
                if (RequireAccessLevel(UserAccessLevel.Admin, user))
                    return true;

                // People can see their own info
                var id = groupName.Split('_').Last();

                if (!long.TryParse(id, out long idNumber))
                {
                    return false;
                }

                return idNumber == user?.Id;
            }

            // Only admins see this
            if (groupName.StartsWith(NotificationGroups.UserUpdatedPrefixAdminInfo))
                return RequireAccessLevel(UserAccessLevel.Admin, user);

            // Unknown groups are not allowed
            return false;
        }

        private static bool RequireAccessLevel(UserAccessLevel level, User user)
        {
            // All site visitors have the not logged in access level
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
