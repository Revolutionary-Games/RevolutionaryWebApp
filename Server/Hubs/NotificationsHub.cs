namespace ThriveDevCenter.Server.Hubs
{
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.SignalR;
    using Shared;

    public class NotificationsHub : Hub<INotifications>
    {
        public override async Task OnConnectedAsync()
        {
            // TODO: implement sending mismatching version notification
            if (false)
            {
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
            return Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        }

        public Task SendSiteNotice(string type, string message)
        {
            return Clients.All.ReceiveSiteNotice(type, message);
        }
    }

    public interface INotifications
    {
        Task ReceiveSiteNotice(string type, string message);
        Task ReceiveUpdatedLFS(LFSProjectInfo item);
        Task ReceiveSessionInvalidation();
        Task ReceiveVersionMismatch();
    }
}
