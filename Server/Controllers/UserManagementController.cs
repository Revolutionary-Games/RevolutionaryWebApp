using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ThriveDevCenter.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Hubs;
using Microsoft.AspNetCore.SignalR;
using Authorization;
using BlazorPagination;
using Filters;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared;
using Shared.Models;
using Shared.Notifications;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class UserManagementController : Controller
{
    private readonly ILogger<UserManagementController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IHubContext<NotificationsHub, INotifications> notifications;

    public UserManagementController(ILogger<UserManagementController> logger,
        NotificationsEnabledDb database, IHubContext<NotificationsHub, INotifications> notifications)
    {
        this.logger = logger;
        this.database = database;
        this.notifications = notifications;
    }

    [HttpGet]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
    public async Task<PagedResult<UserInfo>> Get([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize)
    {
        IQueryable<User> query;

        try
        {
            query = database.Users.AsNoTracking().OrderBy(sortColumn, sortDirection, new[] { "UserName" });
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        // TODO: create a separate UserInfo type to use for the list here
        return objects.ConvertResult(i => i.GetInfo(RecordAccessLevel.Admin));
    }

    [HttpGet("{id:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<UserInfo>> GetUser([Required] long id)
    {
        bool admin =
            HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None);

        var user = await database.Users.AsNoTracking().Where(u => u.Id == id).Include(u => u.AssociationMember)
            .FirstOrDefaultAsync();

        if (user == null)
            return NotFound();

        // Has to be an admin or looking at their own data
        if (!admin && HttpContext.AuthenticatedUser()!.Id != user.Id)
            return NotFound();

        return user.GetInfo(admin ? RecordAccessLevel.Admin : RecordAccessLevel.Private);
    }

    // TODO: should this be allowed for non-logged in users so that the uploader names in public folder items
    // could be shown?

    [HttpPost("usernames")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<Dictionary<long, string>>> GetUsernames([Required] [FromBody] List<long> ids)
    {
        var users = await database.Users.Where(u => ids.Contains(u.Id))
            .Select(u => new Tuple<long, string>(u.Id, u.UserName)).ToListAsync();

        Dictionary<long, string> result = new();

        foreach (var (id, username) in users)
        {
            result.Add(id, username);
        }

        // Add missing text for users we didn't find
        foreach (var requestedId in ids)
        {
            if (!result.ContainsKey(requestedId))
            {
                result.Add(requestedId, $"unknown user ({requestedId})");
            }
        }

        return result;
    }

    [HttpPost("pickUser")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<Dictionary<long, string>>> FindUserToPick(
        [Required]
        [StringLength(300, MinimumLength = AppInfo.MinNameLengthToLookFor)]
        [FromBody]
        string partialName)
    {
        // TODO: case insensitive matching
        // TODO: fuzzy matching
        var users = await database.Users
            .Where(u => u.UserName == null ? u.Email.Contains(partialName) : u.UserName.Contains(partialName))
            .Select(u => new Tuple<long, string>(u.Id, u.UserName)).ToListAsync();

        return users.ToDictionary(t => t.Item1, t => t.Item2);
    }

    [HttpGet("{id:long}/sessions")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<PagedResult<SessionDTO>>> GetUserSessions([Required] long id,
        [Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 50)] int pageSize)
    {
        bool admin =
            HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None);

        var user = await database.Users.FindAsync(id);

        if (user == null)
            return NotFound();

        // Has to be an admin or looking at their own data
        if (!admin && HttpContext.AuthenticatedUser()!.Id != user.Id)
            return NotFound();

        // If requestSession is null this request came with an API key and not a browser session
        var requestSession = HttpContext.AuthenticatedUserSession();

        IQueryable<Session> query;

        try
        {
            query = database.Sessions.AsNoTracking().Where(s => s.UserId == id).OrderBy(sortColumn, sortDirection);
        }
        catch (ArgumentException e)
        {
            logger.LogWarning("Invalid requested order: {@E}", e);
            throw new HttpResponseException() { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO(i.Id == requestSession?.Id));
    }

    [HttpDelete("{id:long}/sessions")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<PagedResult<SessionDTO>>> DeleteAllUserSessions([Required] long id)
    {
        bool admin =
            HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None);

        var user = await database.Users.FindAsync(id);
        var actingUser = HttpContext.AuthenticatedUser()!;

        if (user == null)
            return NotFound();

        // Has to be an admin or performing an action on their own data
        if (!admin && actingUser.Id != user.Id)
            return NotFound();

        var sessions = await database.Sessions.Where(s => s.UserId == id).ToListAsync();

        if (sessions.Count < 1)
            return Ok();

        if (admin && actingUser.Id != user.Id)
        {
            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = "Forced logout",
                PerformedById = actingUser.Id,
                TargetUserId = user.Id,
            });
        }
        else
        {
            logger.LogInformation("User ({Email}) deleted their sessions from {RemoteIpAddress}", user.Email,
                HttpContext.Connection.RemoteIpAddress);
        }

        database.Sessions.RemoveRange(sessions);
        await database.SaveChangesAsync();

        await InvalidateSessions(sessions.Select(s => s.Id));
        return Ok();
    }

    [HttpDelete("{id:long}/otherSessions")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<PagedResult<SessionDTO>>> DeleteOtherUserSessions([Required] long id)
    {
        var user = await database.Users.FindAsync(id);

        if (user == null)
            return NotFound();

        // It makes sense only for the current user to perform this action
        if (HttpContext.AuthenticatedUser()!.Id != user.Id)
            return NotFound();

        var requestSession = HttpContext.AuthenticatedUserSession();

        if (requestSession == null)
            return Forbid("This action can only be performed with an active session");

        var sessions = await database.Sessions.Where(s => s.UserId == id).ToListAsync();

        if (sessions.Count < 1)
            return Ok();

        sessions = sessions.Where(s => s.Id != requestSession.Id).ToList();

        database.Sessions.RemoveRange(sessions);
        await database.SaveChangesAsync();

        logger.LogInformation("User ({Email}) deleted their other sessions than {Id} from {RemoteIpAddress}",
            user.Email, requestSession.Id, HttpContext.Connection.RemoteIpAddress);

        await InvalidateSessions(sessions.Select(s => s.Id));
        return Ok();
    }

    [HttpDelete("{id:long}/sessions/{sessionId:long}")]
    [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.RestrictedUser)]
    public async Task<ActionResult<PagedResult<SessionDTO>>> DeleteSpecificUserSession([Required] long id,
        [Required] long sessionId)
    {
        bool admin =
            HttpContext.HasAuthenticatedUserWithAccess(UserAccessLevel.Admin, AuthenticationScopeRestriction.None);

        var user = await database.Users.FindAsync(id);
        var actingUser = HttpContext.AuthenticatedUser()!;

        if (user == null)
            return NotFound();

        // Has to be an admin or performing an action on their own data
        if (!admin && actingUser.Id != user.Id)
            return NotFound();

        var sessions = await database.Sessions.Where(s => s.UserId == id).ToListAsync();

        Session? session = null;

        // For safety we never give out the real ids of the session, so we need to here find the actual
        // target session
        foreach (var potentialSession in sessions)
        {
            if (potentialSession.GetDoubleHashedId() == sessionId)
            {
                session = potentialSession;
                break;
            }
        }

        if (session == null)
            return NotFound();

        if (admin && actingUser.Id != user.Id)
        {
            await database.AdminActions.AddAsync(new AdminAction()
            {
                Message = $"Force ended session {session.Id}",
                PerformedById = actingUser.Id,
                TargetUserId = user.Id,
            });
        }
        else
        {
            logger.LogInformation("User ({Email}) deleted their session {Id} from {RemoteIpAddress}",
                user.Email, session.Id, HttpContext.Connection.RemoteIpAddress);
        }

        database.Sessions.Remove(session);
        await database.SaveChangesAsync();

        await InvalidateSessions(new[] { session.Id });
        return Ok();
    }

    [NonAction]
    private async Task InvalidateSessions(IEnumerable<Guid> sessions)
    {
        // This per-user variant didn't end up being needed currently (this probably works, but is not tested)
        // await notifications.Clients.User(userId.ToString()).ReceiveSessionInvalidation();

        await notifications.Clients.Groups(sessions.Select(s => NotificationGroups.SessionImportantMessage + s))
            .ReceiveSessionInvalidation();

        // TODO: force close signalr connections for the user https://github.com/dotnet/aspnetcore/issues/5333
    }
}