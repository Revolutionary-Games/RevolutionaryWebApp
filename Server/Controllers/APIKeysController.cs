namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]/{userId:long}")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
public class APIKeysController(NotificationsEnabledDb database, ILogger<APIKeysController> logger) : Controller
{
    [HttpGet]
    public async Task<ActionResult<APIKeyDTO[]>> List(long userId)
    {
        if (!CanManage(userId))
            return Forbid();

        var keys = await database.APIKeys.AsNoTracking().Where(k => k.UserId == userId)
            .OrderBy(k => k.CreatedAt).ToListAsync();
        return keys.Select(k => k.GetDTO()).ToArray();
    }

    [HttpPost]
    public async Task<ActionResult<CreatedAPIKey>> Create(long userId, [FromBody] APIKeySettings request)
    {
        var user = HttpContext.AuthenticatedUserOrThrow();
        if (!CanManage(userId) || user.Id != userId)
            return Forbid();

        var groups = user.AccessCachedGroupsOrThrow();
        if (APIKeyPolicy.Limit(groups) == 0)
            return BadRequest("API key limit reached");

        var error = Validate(request, groups);
        if (error != null)
            return BadRequest(error);

        // Serialize creates per owner so concurrent requests cannot exceed the account limit.
        await using var transaction = await database.Database.BeginTransactionAsync();
        await database.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({userId})");
        if (await database.APIKeys.CountAsync(k => k.UserId == userId) >= APIKeyPolicy.Limit(groups))
            return Conflict("API key limit reached");

        var key = new APIKey
        {
            Id = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 14),
            Secret = Guid.NewGuid().ToString(),
            UserId = userId,
        };

        Apply(key, request);
        await database.APIKeys.AddAsync(key);

        await database.ActionLogEntries.AddAsync(new ActionLogEntry("User created an API key")
        {
            PerformedById = userId,
        });

        await database.SaveChangesAsync();
        await transaction.CommitAsync();

        logger.LogInformation("User {Email} created API key {KeyId}", user.Email, key.Id);
        Response.Headers.CacheControl = "no-store";

        return new CreatedAPIKey { Authorization = $"key:{key.Id}:{key.Secret}" };
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long userId, string id, [FromBody] APIKeySettings request)
    {
        if (!CanManage(userId))
            return Forbid();

        var owner = await database.Users.FindAsync(userId);
        if (owner == null)
            return NotFound();

        await owner.ComputeUserGroups(database);
        var error = Validate(request, owner.AccessCachedGroupsOrThrow());
        if (error != null)
            return BadRequest(error);

        var key = await database.APIKeys.WhereHashed(nameof(APIKey.Id), id).Where(k => k.UserId == userId)
            .AsAsyncEnumerable().FirstOrDefaultAsync(k => SecurityHelpers.SlowEquals(k.Id, id));
        if (key == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        Apply(key, request);
        await RecordAdminAction(userId, $"Updated API key {id}");
        await database.SaveChangesAsync();

        logger.LogInformation("User {Email} updated API key {KeyId}", user.Email, id);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long userId, string id)
    {
        if (!CanManage(userId))
            return Forbid();

        var key = await database.APIKeys.WhereHashed(nameof(APIKey.Id), id).Where(k => k.UserId == userId)
            .AsAsyncEnumerable().FirstOrDefaultAsync(k => SecurityHelpers.SlowEquals(k.Id, id));
        if (key == null)
            return NotFound();

        var user = HttpContext.AuthenticatedUserOrThrow();

        database.APIKeys.Remove(key);
        await RecordAdminAction(userId, $"Deleted API key {id}");
        await database.SaveChangesAsync();

        logger.LogInformation("User {ActorId} deleted API key {KeyId}", user.Id, id);
        return Ok();
    }

    private static string? Validate(APIKeySettings request, IUserGroupData groups)
    {
        if (!request.ReadOnly && APIKeyPolicy.IsReadOnlyUser(groups))
            return "This user may only have read-only API keys";

        if (request.Scopes == null! || request.Scopes.Length == 0 || request.Scopes.Any(s => !Enum.IsDefined(s)))
            return "Select at least one valid scope";

        if (!string.IsNullOrWhiteSpace(request.AllowedIPNetwork) &&
            !IPNetwork.TryParse(request.AllowedIPNetwork, out _))
        {
            return "Allowed IP addresses must be a network in CIDR notation (for example 192.0.2.0/24)";
        }

        return null;
    }

    private static void Apply(APIKey key, APIKeySettings request)
    {
        key.Scopes = request.Scopes.Contains(APIKeyScope.All) ?
            "0" : string.Join(',', request.Scopes.Distinct().Order().Select(s => (int)s));
        key.ReadOnly = request.ReadOnly;
        key.ExpiresAt = request.ExpiresAt?.ToUniversalTime();
        key.AllowedIPNetwork = string.IsNullOrWhiteSpace(request.AllowedIPNetwork) ? null : request.AllowedIPNetwork;
    }

    private async Task RecordAdminAction(long userId, string description)
    {
        var actor = HttpContext.AuthenticatedUserOrThrow();
        if (actor.Id == userId)
            return;

        await database.AdminActions.AddAsync(new AdminAction(description)
        {
            TargetUserId = userId,
            PerformedById = actor.Id,
        });
    }

    private bool CanManage(long userId)
    {
        var user = HttpContext.AuthenticatedUserOrThrow();

        // User session is checked so that API key cannot manage other API keys
        return HttpContext.AuthenticatedUserSession() != null &&
            (user.Id == userId || user.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Admin));
    }
}
