namespace ThriveDevCenter.Server.Controllers;

using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Forms;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/[controller]")]
public class TokensController : Controller
{
    private readonly ILogger<TokensController> logger;
    private readonly ApplicationDbContext database;

    public TokensController(ILogger<TokensController> logger, NotificationsEnabledDb database)
    {
        this.logger = logger;
        this.database = database;
    }

    [HttpGet("api/self")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public ActionResult<string> GetOwnAPIToken()
    {
        return HttpContext.AuthenticatedUser()!.ApiToken ?? "none";
    }

    [HttpGet("lfs/self")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public ActionResult<string> GetOwnLFSToken()
    {
        return HttpContext.AuthenticatedUser()!.LfsToken ?? "none";
    }

    [HttpDelete("api/self")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> DeleteOwnAPIToken()
    {
        // We must re-fetch this data to get it from our db context for updating it
        var user = await database.Users.FindAsync(HttpContext.AuthenticatedUserOrThrow().Id);
        if (user == null)
            return Problem("Could not find authenticated user in the database");

        await user.ComputeUserGroups(database);

        logger.LogInformation("User ({Email}) deleted their own API token", user.Email);

        await database.LogEntries.AddAsync(new LogEntry
        {
            Message = "API token cleared by user",
            TargetUserId = user.Id,
        });

        user.ApiToken = null;
        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("lfs/self")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<IActionResult> DeleteOwnLFSToken()
    {
        // We must re-fetch this data to get it from our db context for updating it
        var user = await database.Users.FindAsync(HttpContext.AuthenticatedUserOrThrow().Id);
        if (user == null)
            return Problem("Could not find authenticated user in the database");

        await user.ComputeUserGroups(database);

        logger.LogInformation("User ({Email}) deleted their own LFS token", user.Email);

        await database.LogEntries.AddAsync(new LogEntry
        {
            Message = "LFS token cleared by user",
            TargetUserId = user.Id,
        });

        user.LfsToken = null;
        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpPost("api/self")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<string>> CreateOwnAPIToken()
    {
        // We must re-fetch this data to get it from our db context for updating it
        var user = await database.Users.FindAsync(HttpContext.AuthenticatedUserOrThrow().Id);
        if (user == null)
            return Problem("Could not find authenticated user in the database");

        await user.ComputeUserGroups(database);

        logger.LogInformation("User ({Email}) created a new API token", user.Email);

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = "API token created",
            PerformedById = user.Id,
        });

        user.ApiToken = NonceGenerator.GenerateNonce(AppInfo.APITokenByteCount);
        await database.SaveChangesAsync();

        return user.ApiToken;
    }

    [HttpPost("lfs/self")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    public async Task<ActionResult<string>> CreateOwnLFSToken()
    {
        // We must re-fetch this data to get it from our db context for updating it
        var user = await database.Users.FindAsync(HttpContext.AuthenticatedUserOrThrow().Id);
        if (user == null)
            return Problem("Could not find authenticated user in the database");

        await user.ComputeUserGroups(database);

        logger.LogInformation("User ({Email}) created a new LFS token", user.Email);

        await database.ActionLogEntries.AddAsync(new ActionLogEntry
        {
            Message = "LFS token created",
            PerformedById = user.Id,
        });

        user.LfsToken = NonceGenerator.GenerateNonce(AppInfo.APITokenByteCount);
        await database.SaveChangesAsync();

        return user.LfsToken;
    }

    [HttpPost("clear")]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    public async Task<ActionResult<string>> ForceClearTokens([FromBody] ForceClearTokensForm request)
    {
        var target = await database.Users.FindAsync(request.TargetUserId);

        if (target == null)
            return NotFound();

        // Early exit if already cleared
        if (target.LfsToken == null && target.ApiToken == null)
            return Ok("Tokens already cleared");

        var user = HttpContext.AuthenticatedUser()!;
        logger.LogInformation("Force clearing tokens on user {Id} by admin {Email}", target.Id, user.Email);

        await database.AdminActions.AddAsync(new AdminAction
        {
            Message = "Force cleared user's tokens",
            TargetUserId = target.Id,
            PerformedById = user.Id,
        });

        // It's assumed here that the authentication also used ApplicationDbContext so that this works
        target.LfsToken = null;
        target.ApiToken = null;

        await database.SaveChangesAsync();

        return Ok("Tokens cleared");
    }
}
