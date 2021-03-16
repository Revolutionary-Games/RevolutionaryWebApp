namespace ThriveDevCenter.Server.Authorization
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Models;
    using Shared;

    public static class UserFromCookiesHelper
    {
        /// <summary>
        ///   Loads a user from session cookie
        /// </summary>
        /// <param name="cookies">Cookies to read the session cookie from</param>
        /// <param name="database">Where to get users from</param>
        /// <returns>The user for the session cookie or null</returns>
        /// <exception cref="ArgumentException">If the cookie is malformed</exception>
        public static Task<User> GetUserFromSession(this IRequestCookieCollection cookies,
            ApplicationDbContext database)
        {
            if (!cookies.TryGetValue(AppInfo.SessionCookieName, out string session) || string.IsNullOrEmpty(session))
                return Task.FromResult<User>(null);

            return GetUserFromSession(session, database);
        }

        public static async Task<User> GetUserFromSession(string sessionId, ApplicationDbContext database,
            bool updateLastUsed = true)
        {
            var existingSession = await GetSession(sessionId, database);

            // No user if the session was not found, or the session was invalidated
            if (existingSession?.User == null || existingSession.SessionVersion != existingSession.User.SessionVersion)
                return null;

            // TODO: should this suspended check be here?
            if (existingSession.User.Suspended == true)
                return null;

            if (updateLastUsed)
            {
                // TODO: perform in a background job?
                existingSession.LastUsed = DateTime.UtcNow;
                await database.SaveChangesAsync();
            }

            return existingSession.User;
        }

        public static async Task<Session> GetSession(string sessionId, ApplicationDbContext database)
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
                throw new ArgumentException("invalid session format");
            }

            return await database.Sessions.Where(s => s.Id == parsed).Include(s => s.User).FirstOrDefaultAsync();
        }

        public static Task<Session> GetSession(this IRequestCookieCollection cookies,
            ApplicationDbContext database)
        {
            if (!cookies.TryGetValue(AppInfo.SessionCookieName, out string session) || string.IsNullOrEmpty(session))
                return Task.FromResult<Session>(null);

            return GetSession(session, database);
        }
    }
}
