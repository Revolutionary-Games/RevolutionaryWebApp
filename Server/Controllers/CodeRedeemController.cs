namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using Utilities;

[ApiController]
[Route("api/v1/[controller]")]
public class CodeRedeemController : Controller
{
    private readonly ILogger<CodeRedeemController> logger;
    private readonly NotificationsEnabledDb database;
    private readonly IBackgroundJobClient jobClient;

    public CodeRedeemController(ILogger<CodeRedeemController> logger, NotificationsEnabledDb database,
        IBackgroundJobClient jobClient)
    {
        this.logger = logger;
        this.database = database;
        this.jobClient = jobClient;
    }

    [HttpPost]
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    [EnableRateLimiting(RateLimitCategories.CodeRedeemLimit)]
    public async Task<IActionResult> Redeem([Required] RedeemCodeData data)
    {
        var target = await database.Users.Include(u => u.Groups)
            .FirstAsync(u => u.Id == HttpContext.AuthenticatedUserOrThrow().Id);

        if (target == null)
            throw new Exception("User not found after authorization");

        if (string.IsNullOrEmpty(data.Code) || data.Code.Length < AppInfo.MinimumRedeemableCodeLength)
            return BadRequest("The code is too short");

        if (!Guid.TryParse(data.Code, out Guid parsedCode))
            return BadRequest("Invalid code format");

        var validCode = await database.RedeemableCodes.WhereHashed(nameof(RedeemableCode.Id), data.Code)
            .ToAsyncEnumerable().FirstOrDefaultAsync(c => c.Id == parsedCode);

        if (validCode == null)
            return BadRequest("Invalid code");

        string granted;

        switch (validCode.GrantedResource)
        {
            case "GroupAdmin":
            {
                if (target.Groups.Any(g => g.Id == GroupType.Admin))
                    return GetAlreadyGotResult();

                var admin = await database.UserGroups.FindAsync(GroupType.Admin) ??
                    throw new Exception("Admin group not found");

                target.Groups.Add(admin);
                target.OnGroupsChanged(jobClient);
                granted = "admin group membership";

                break;
            }

            case "GroupDeveloper":
            {
                if (target.Groups.Any(g => g.Id == GroupType.Developer))
                    return GetAlreadyGotResult();

                var developer = await database.UserGroups.FindAsync(GroupType.Developer) ??
                    throw new Exception("Developer group not found");

                target.Groups.Add(developer);
                target.OnGroupsChanged(jobClient);
                granted = "developer group membership";

                break;
            }

            default:
                logger.LogError("Redeemable code has invalid resource: {GrantedResource}",
                    validCode.GrantedResource);
                return Problem("Code has invalid resource to be granted");
        }

        // Delete single use codes
        if (!validCode.MultiUse)
        {
            database.RedeemableCodes.Remove(validCode);
        }
        else
        {
            validCode.Uses += 1;
        }

        await database.ActionLogEntries.AddAsync(
            new ActionLogEntry($"Granted {granted} for redeeming a redeemable code to self")
            {
                PerformedById = target.Id,
            });

        await database.SaveChangesAsync();

        logger.LogInformation("Code: {Code} has been redeemed by {Email}, granting: {Granted}", data.Code,
            target.Email, granted);

        return Ok($"You have been granted: {granted}");
    }

    [NonAction]
    private IActionResult GetAlreadyGotResult()
    {
        return Conflict("You already have the resource you were trying to redeem");
    }
}
