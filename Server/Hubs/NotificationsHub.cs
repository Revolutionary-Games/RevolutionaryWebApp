namespace ThriveDevCenter.Server.Hubs
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.Extensions.Primitives;
    using Shared;

    public class NotificationsHub : Hub<INotifications>
    {
        public override async Task OnConnectedAsync()
        {
            var http = Context.GetHttpContext();

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

                bool invalidVersion = false;

                try
                {
                    var major = Convert.ToInt32(majorStr[0]);
                    var minor = Convert.ToInt32(minorStr[0]);

                    if (major != AppVersion.Major || minor != AppVersion.Minor)
                        invalidVersion = true;
                }
                catch (Exception)
                {
                    throw new HubException("invalid connection parameters");
                }

                if (invalidVersion)
                    await Clients.Caller.ReceiveVersionMismatch();
            }

            await base.OnConnectedAsync();
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
            return Clients.All.ReceiveSiteNotice(type, message);
        }
    }

    public interface INotifications
    {
        Task ReceiveSiteNotice(SiteNoticeType type, string message);
        Task ReceiveUpdatedLFS(LFSProjectInfo item);
        Task ReceiveSessionInvalidation();
        Task ReceiveVersionMismatch();
    }
}
