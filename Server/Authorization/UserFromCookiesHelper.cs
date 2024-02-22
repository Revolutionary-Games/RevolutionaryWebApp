namespace RevolutionaryWebApp.Server.Authorization;

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared;
using Utilities;

public static class UserFromCookiesHelper
{
    /// <summary>
    ///   Loads a user from session cookie
    /// </summary>
    /// <param name="cookies">Cookies to read the session cookie from</param>
    /// <param name="database">Where to get users from</param>
    /// <param name="clientAddress">The address the cookies are from (used to track where session is used)</param>
    /// <returns>The user for the session cookie or null and the session that the user was retrieved from</returns>
    /// <exception cref="ArgumentException">If the cookie is malformed</exception>
    public static Task<(User? User, Session? Session)> GetUserFromSession(this IRequestCookieCollection cookies,
        ApplicationDbContext database, IPAddress? clientAddress)
    {
        if (!cookies.TryGetValue(AppInfo.SessionCookieName, out string? sessionRaw) || string.IsNullOrEmpty(sessionRaw))
            return Task.FromResult<(User?, Session?)>((null, null));

        var data = sessionRaw.Split(':', 2);

        if (data.Length != 2)
            return Task.FromResult<(User?, Session?)>((null, null));

        if (!long.TryParse(data[1], out var expectedUserId))
            return Task.FromResult<(User?, Session?)>((null, null));

        return GetUserFromSession(data[0], expectedUserId, database, true, clientAddress);
    }

    public static async Task<(User? User, Session? Session)> GetUserFromSession(string sessionId, long expectedId,
        ApplicationDbContext database, bool updateLastUsed = true, IPAddress? clientAddress = null)
    {
        var existingSession = await GetSession(sessionId, expectedId, database);

        return (await GetUserFromSession(existingSession, database, updateLastUsed, clientAddress),
            existingSession);
    }

    public static async Task<User?> GetUserFromSession(Session? existingSession,
        ApplicationDbContext database, bool updateLastUsed = true, IPAddress? clientAddress = null)
    {
        // No user if the session was not found
        if (existingSession?.User == null)
            return null;

        // There's now a bunch of code that relies on suspended users being disallowed here
        // TODO: should this suspended check be here? At least now NotificationsHub depends on this
        if (existingSession.User.Suspended == true)
            return null;

        // TODO: should non-user sessions be able to update the last used time?
        if (updateLastUsed)
        {
            var now = DateTime.UtcNow;

            // For performance optimization, last used isn't updated always
            if (now - existingSession.LastUsed >= AppInfo.LastUsedSessionAccuracy ||
                existingSession.LastUsedFrom == null ||
                !existingSession.LastUsedFrom.Equals(clientAddress))
            {
                // TODO: perform in a background job?
                existingSession.LastUsed = now;
                existingSession.LastUsedFrom = clientAddress;
                await database.SaveChangesAsync();
            }
        }

        // Allow everything checking accessing user to get info on the user's groups
        existingSession.User?.SetGroupsFromSessionCache(existingSession);

        return existingSession.User;
    }

    public static async Task<Session?> GetSession(string sessionId, long expectedUserId, ApplicationDbContext database)
    {
        // TODO: maybe it should be configurable if the user info should be fetched, as in some cases it might be
        // not needed
        Guid parsed;

        try
        {
            parsed = Guid.Parse(sessionId);
        }
        catch (Exception)
        {
            // Instead of spamming exceptions on invalid data, just ignore
            return null;
        }

        var session = await database.Sessions.WhereHashed(nameof(Session.Id), sessionId).Include(s => s.User)
            .ThenInclude(u => u!.AssociationMember)
            .ToAsyncEnumerable().FirstOrDefaultAsync(s => s.Id == parsed);

        if (session == null)
            return null;

        // If the user doesn't match the expected id, fail even if the session was otherwise fine
        if (session.User == null && expectedUserId != -1)
            return null;

        if (session.User != null && session.User.Id != expectedUserId)
            return null;

        return session;
    }

    public static Task<Session?> GetSession(this IRequestCookieCollection cookies,
        ApplicationDbContext database)
    {
        if (!cookies.TryGetValue(AppInfo.SessionCookieName, out string? sessionRaw) || string.IsNullOrEmpty(sessionRaw))
            return Task.FromResult<Session?>(null);

        var data = sessionRaw.Split(':', 2);

        if (data.Length != 2)
            return Task.FromResult<Session?>(null);

        if (!long.TryParse(data[1], out var expectedUserId))
            return Task.FromResult<Session?>(null);

        return GetSession(data[0], expectedUserId, database);
    }
}
