namespace RevolutionaryWebApp.Server.Services;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Notifications;

/// <summary>
///   Revokes local sessions and provides the integration point for future forum SSO sessions.
/// </summary>
public class ModerationSessionService
{
    private readonly NotificationsEnabledDb database;
    private readonly IHubContext<NotificationsHub, INotifications> notifications;
    private readonly List<Guid> revokedSessions = new();
    private readonly HashSet<long> revokedUsers = new();

    public ModerationSessionService(NotificationsEnabledDb database,
        IHubContext<NotificationsHub, INotifications> notifications)
    {
        this.database = database;
        this.notifications = notifications;
    }

    public async Task RevokeSessions(long userId)
    {
        var sessions = await database.Sessions.Where(session =>
            session.UserId == userId || session.PendingTwoFactorUserId == userId).ToListAsync();
        database.Sessions.RemoveRange(sessions);
        revokedSessions.AddRange(sessions.Select(session => session.Id));
        revokedUsers.Add(userId);
    }

    public async Task CompleteRevocation()
    {
        if (revokedSessions.Count > 0)
        {
            await notifications.Clients.Groups(revokedSessions.Select(id =>
                NotificationGroups.SessionImportantMessage + id)).ReceiveSessionInvalidation();
        }

        foreach (var userId in revokedUsers)
            await InvalidateForumSessions(userId);
    }

    public virtual Task InvalidateForumSessions(long userId)
    {
        // When this application becomes an SSO source, revoke the relying forum's sessions here as well.
        // TODO: user forum account management
        _ = userId;
        return Task.CompletedTask;
    }
}
