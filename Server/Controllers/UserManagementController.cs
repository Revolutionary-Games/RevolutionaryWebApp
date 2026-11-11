namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Authorization;
using BlazorPagination;
using Filters;
using Hubs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;
using Shared.Notifications;
using Utilities;

[APIKeyScope(APIKeyScope.Users)]
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

    [HttpGet("profile/{username}")]
    public async Task<ActionResult<PublicUserProfile>> GetPublicProfile(string username)
    {
        bool loggedIn = HttpContext.AuthenticatedUser() != null;
        var users = await database.Users.AsNoTracking().Where(u => u.UserName == username)
            .Select(u => new PublicUserProfile
            {
                Id = u.Id, Name = u.UserName, CreatedAt = u.CreatedAt,
                DisplayName = loggedIn ? u.DisplayName : null,
            }).Take(2).ToListAsync();

        if (users.Count != 1)
            return NotFound();

        return users[0];
    }

    [HttpGet("mentionSuggestions")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    [OutputCache(PolicyName = "Authenticated", Duration = 300)]
    public async Task<ActionResult<List<string>>> GetMentionSuggestions(
        [Required] [StringLength(50, MinimumLength = 3)] string query)
    {
        if (!Shared.Utilities.Usernames.IsValid(query) || query.Length < 3)
            return BadRequest("Type at least 3 username characters");

        var term = query.ToLowerInvariant();
        return await database.Users.AsNoTracking().Where(u => u.UserName.ToLower().Contains(term))
            .GroupBy(u => u.UserName).Where(g => g.Count() == 1).Select(g => g.Key)
            .OrderByDescending(name => name.ToLower().StartsWith(term)).ThenBy(name => name)
            .Take(10).ToListAsync(HttpContext.RequestAborted);
    }

    [HttpPost("mentions")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<List<string>>> ResolveMentions([FromBody] List<string> names)
    {
        if (names.Count > 10 || names.Any(n => !Shared.Utilities.Usernames.IsValid(n)))
            return BadRequest("Request at most 10 valid usernames");

        var matches = await database.Users.AsNoTracking().Where(u => names.Contains(u.UserName))
            .Select(u => u.UserName).ToListAsync();
        return matches.GroupBy(n => n).Where(g => g.Count() == 1).Select(g => g.Key).ToList();
    }

    // This implements a custom access filter
    [HttpGet]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    public async Task<ActionResult<PagedResult<UserInfo>>> GetList([Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 100)] int pageSize, [FromQuery] string? search)
    {
        if (!CanViewUserDetails())
            return Forbid();

        IQueryable<User> query;

        bool suspendedSearch = false;

        // Emulate suspended search as it is a calculated column
        if (sortColumn == nameof(Models.User.Suspended))
        {
            sortColumn = nameof(Models.User.UserName);
            suspendedSearch = true;
        }

        try
        {
            query = database.Users.AsNoTracking().OrderBy(sortColumn, sortDirection, new[] { "UserName" });
        }
        catch (ArgumentException e)
        {
            logger.LogWarning(e, "Invalid requested order");
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        if (suspendedSearch)
        {
            var now = DateTime.UtcNow;

            if (sortDirection == SortDirection.Ascending)
            {
                query = query.OrderBy(u => u.SuspendedUntil != null && u.SuspendedUntil > now);
            }
            else
            {
                query = query.OrderByDescending(u => u.SuspendedUntil != null && u.SuspendedUntil > now);
            }
        }

        // Apply optional search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();

            query = query.Where(u =>
                u.UserName.ToLower().Contains(term) ||
                (u.DisplayName != null && u.DisplayName.ToLower().Contains(term)) ||
                u.Email.ToLower().Contains(term) ||
                (u.NormalizedEmail != null && u.NormalizedEmail.Contains(term)));
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        // TODO: create a separate UserInfo type to use for the list here
        return objects.ConvertResult(i => i.GetInfo(RecordAccessLevel.Admin));
    }

    [HttpGet("{id:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<UserDTO>> GetUser([Required] long id)
    {
        var user = await database.Users.AsNoTracking().Where(u => u.Id == id).Include(u => u.AssociationMember)
            .Include(u => u.ContributionCount).FirstOrDefaultAsync();

        if (user == null)
            return NotFound();

        // Staff can inspect accounts; users can inspect their own data.
        if (!CanViewUserDetails() && HttpContext.AuthenticatedUser()!.Id != user.Id)
            return NotFound();

        await user.ComputeUserGroups(database);

        return user.GetDTO(CanViewUserDetails() ? RecordAccessLevel.Admin : RecordAccessLevel.Private);
    }

    [HttpPut("{id:long}/name")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    [CountContribution(200)]
    public async Task<ActionResult> UpdateName(long id, [FromBody] UserNameUpdateRequest request)
    {
        var actingUser = HttpContext.AuthenticatedUserOrThrow();
        bool admin = HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None);

        if (!admin && actingUser.Id != id)
            return NotFound();

        var user = await database.Users.FindAsync(id);
        if (user == null)
            return NotFound();

        if (request.DisplayName != null && !HasValidNameCharacters(request.DisplayName))
        {
            return BadRequest("Name may contain letters, accents, numbers, ordinary spaces, apostrophes, " +
                "hyphens, periods and middle dots, but no control characters, emoji or other symbols");
        }

        var name = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim();
        if (name != null && name.Length > 120)
            return BadRequest("Name must be at most 120 characters");

        // TODO: maybe we should allow duplicate names?
        if (name != null && await database.Users.AnyAsync(u => u.Id != id && u.DisplayName == name))
            return Conflict("This name is already in use");

        if (user.DisplayName == name)
            return Ok();

        await user.ComputeUserGroups(database);
        user.DisplayName = name;

        if (actingUser.Id != id)
        {
            await database.AdminActions.AddAsync(new AdminAction("Account name changed")
            {
                PerformedById = actingUser.Id,
                TargetUserId = id,
            });
        }

        await database.SaveChangesAsync();
        logger.LogInformation("Account name for user {Id} changed by user {Email}", id, actingUser.Email);
        return Ok();
    }

    [APIKeyWrite(false)]
    [HttpPost("usernames")]
    public async Task<ActionResult<Dictionary<long, string>>> GetUsernames([Required] [FromBody] List<long> ids)
    {
        if (ids.Count > 1000)
            return BadRequest("Too big request");

        if (ids.Count == 0)
            return new Dictionary<long, string>();

        if (ids.Count > 100 && HttpContext.AuthenticatedUser() == null)
            return BadRequest("Can't request that many IDs");

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

    [APIKeyWrite(false)]
    [HttpPost("pickUser")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<Dictionary<long, string>>> FindUserToPick(
        [Required] [StringLength(300, MinimumLength = AppInfo.MinNameLengthToLookFor)] [FromBody] string partialName)
    {
        // TODO: case-insensitive matching
        // TODO: fuzzy matching
        var users = await database.Users
            .Where(u => u.UserName.Contains(partialName) ||
                (u.DisplayName != null && u.DisplayName.Contains(partialName)))
            .Select(u => new Tuple<long, string>(u.Id, u.UserName)).ToListAsync();

        return users.ToDictionary(t => t.Item1, t => t.Item2);
    }

    [HttpGet("{id:long}/sessions")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<PagedResult<SessionDTO>>> GetUserSessions([Required] long id,
        [Required] string sortColumn,
        [Required] SortDirection sortDirection, [Required] [Range(1, int.MaxValue)] int page,
        [Required] [Range(1, 50)] int pageSize)
    {
        bool admin =
            HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None);

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
            logger.LogWarning(e, "Invalid requested order");
            throw new HttpResponseException { Value = "Invalid data selection or sort" };
        }

        var objects = await query.ToPagedResultAsync(page, pageSize);

        return objects.ConvertResult(i => i.GetDTO(i.Id == requestSession?.Id));
    }

    [HttpDelete("{id:long}/sessions")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<PagedResult<SessionDTO>>> DeleteAllUserSessions([Required] long id)
    {
        bool admin =
            HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None);

        var user = await database.Users.FindAsync(id);
        var actingUser = HttpContext.AuthenticatedUser()!;

        if (user == null)
            return NotFound();

        // Has to be an admin or performing an action on their own data
        if (!admin && actingUser.Id != user.Id)
            return NotFound();

        await LogoutEverywhere(user, actingUser);
        return Ok();
    }

    [HttpDelete("{id:long}/otherSessions")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
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

        // TODO: account security event log

        database.Sessions.RemoveRange(sessions);
        await database.SaveChangesAsync();

        logger.LogInformation("User ({Email}) deleted their other sessions than {Id} from {RemoteIpAddress}",
            user.Email, requestSession.Id, HttpContext.Connection.RemoteIpAddress);

        var externalLogout = LogoutExternalSessions(user.Id);

        await InvalidateSessions(sessions.Select(s => s.Id));

        await externalLogout;
        return Ok();
    }

    [HttpDelete("{id:long}/sessions/{sessionId:long}")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<PagedResult<SessionDTO>>> DeleteSpecificUserSession([Required] long id,
        [Required] long sessionId)
    {
        bool admin =
            HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None);

        var user = await database.Users.FindAsync(id);
        var actingUser = HttpContext.AuthenticatedUser()!;

        if (user == null)
            return NotFound();

        // Has to be an admin or performing an action on their own data
        if (!admin && actingUser.Id != user.Id)
            return NotFound();

        var sessions = await database.Sessions.Where(s => s.UserId == id).ToListAsync();

        Session? session = null;

        // For safety, we never give out the real ids of the session, so we need to here find the actual
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
            await database.AdminActions.AddAsync(new AdminAction($"Force ended session {session.Id}")
            {
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

    [HttpPost("{id:long}/suspend")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.Admin)]
    [RequireSudoMode]
    public async Task<ActionResult> SuspendUser([Required] long id, [Required] [FromBody] UserSuspendRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Reason not provided");

        var user = await database.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id);
        var actingUser = HttpContext.AuthenticatedUserOrThrow();

        if (user == null)
            return NotFound();

        if (user == actingUser)
            return BadRequest("Cannot suspend self");

        if (user.ProcessGroupDataFromLoadedGroups().HasGroup(GroupType.Admin))
            return BadRequest("Admins cannot be suspended like this");

        if (user.Suspended)
            return BadRequest("User already suspended");

        request.SuspendedUntil = request.SuspendedUntil.ToUniversalTime();

        var earliestUnsuspendTime = DateTime.UtcNow + TimeSpan.FromMinutes(60);

        if (request.SuspendedUntil < earliestUnsuspendTime)
            return BadRequest("Suspended until time is too early (user needs to be suspended for longer)");

        user.SuspendedUntil = request.SuspendedUntil;
        user.SuspendedManually = true;
        user.SuspendedReason = request.Reason;

        await database.AdminActions.AddAsync(new AdminAction(
            $"User {user.UserName} suspended manually for reason \"{request.Reason}\" " +
            $"until {request.SuspendedUntil:yyyy-MM-dd HH:mm:ss}")
        {
            PerformedById = actingUser.Id,
            TargetUserId = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("User ({Email}) suspended by {Email2} for reason: {Reason}",
            user.Email, actingUser.Email, request.Reason);

        try
        {
            await LogoutEverywhere(user, actingUser);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to logout user everywhere when suspending");
            return Problem("Failed to logout user everywhere, but suspension was successful");
        }

        return Ok("User suspended");
    }

    [HttpPost("{id:long}/unsuspend")]
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.Admin)]
    public async Task<ActionResult> UnsuspendUser([Required] long id)
    {
        var user = await database.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id);
        var actingUser = HttpContext.AuthenticatedUserOrThrow();

        if (user == null)
            return NotFound();

        if (user.SuspendedUntil == null)
            return BadRequest("User is not suspended");

        if (user == actingUser)
            throw new Exception("User somehow was able to try to unsuspend self");

        // Groups need to be loaded for notifications
        user.ProcessGroupDataFromLoadedGroups();

        user.SuspendedManually = false;
        user.SuspendedUntil = null;

        await database.AdminActions.AddAsync(new AdminAction("User unsuspended manually")
        {
            PerformedById = actingUser.Id,
            TargetUserId = user.Id,
        });

        await database.SaveChangesAsync();

        logger.LogInformation("User ({Email}) unsuspended by {Email2}", user.Email, actingUser.Email);

        return Ok();
    }

    private static bool HasValidNameCharacters(string name)
    {
        foreach (var rune in name.EnumerateRunes())
        {
            // Variation selectors can turn otherwise ordinary characters into emoji presentations.
            if (rune.Value is (>= 0xFE00 and <= 0xFE0F) or (>= 0xE0100 and <= 0xE01EF))
                return false;

            if (rune.Value is ' ' or '\'' or '-' or '.' or 0x2019 or 0x00B7 or 0x30FB)
                continue;

            var category = Rune.GetUnicodeCategory(rune);
            if (category is not (UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
                UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
                UnicodeCategory.DecimalDigitNumber))
            {
                return false;
            }
        }

        return true;
    }

    [NonAction]
    private bool CanViewUserDetails()
    {
        return HttpContext.HasAuthenticatedUserWithGroup(GroupType.Admin, AuthenticationScopeRestriction.None) ||
            HttpContext.HasAuthenticatedUserWithGroup(GroupType.ContentModerator, AuthenticationScopeRestriction.None);
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

    private async Task LogoutEverywhere(User user, User actingUser)
    {
        var id = user.Id;
        var sessions = await database.Sessions.Where(s => s.UserId == id).ToListAsync();

        var externalLogout = LogoutExternalSessions(user.Id);

        if (sessions.Count < 1)
        {
            // This doesn't try to log out the user from external sources, as the user must have a session to have
            // triggered this action
            await externalLogout;
            return;
        }

        if (actingUser.Id != user.Id)
        {
            if (!actingUser.AccessCachedGroupsOrThrow().HasGroup(GroupType.Admin))
                throw new Exception("Somehow non-admin is logging out another user");

            await database.AdminActions.AddAsync(new AdminAction("Forced logout")
            {
                PerformedById = actingUser.Id,
                TargetUserId = user.Id,
            });
        }
        else
        {
            logger.LogInformation("User ({Email}) deleted their sessions from {RemoteIpAddress}", user.Email,
                HttpContext.Connection.RemoteIpAddress);

            // TODO: account security event log
        }

        database.Sessions.RemoveRange(sessions);
        await database.SaveChangesAsync();

        await InvalidateSessions(sessions.Select(s => s.Id));

        await externalLogout;
    }

    private Task LogoutExternalSessions(long id)
    {
        // TODO: once we use the revolutionary app for forum logins this needs to destroy forum session as well
        _ = id;

        return Task.CompletedTask;
    }
}
