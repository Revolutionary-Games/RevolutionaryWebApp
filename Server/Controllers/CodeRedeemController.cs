using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Threading.Tasks;
    using Authorization;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Utilities;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class CodeRedeemController : Controller
    {
        private readonly ILogger<CodeRedeemController> logger;
        private readonly NotificationsEnabledDb database;

        public CodeRedeemController(ILogger<CodeRedeemController> logger, NotificationsEnabledDb database)
        {
            this.logger = logger;
            this.database = database;
        }

        [AuthorizeRoleFilter]
        [HttpPost]
        public async Task<IActionResult> Redeem([Required] RedeemCodeData data)
        {
            var target = await database.Users.FindAsync(HttpContext.AuthenticatedUser()!.Id);

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
                    if (target.Admin == true)
                        return GetAlreadyGotResult();

                    target.Admin = true;
                    granted = "admin group membership";

                    break;
                }
                case "ForceDeveloper":
                {
                    if (target.Admin == true)
                        target.Admin = false;
                    target.Developer = true;
                    granted = "forced developer group membership";

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

            await database.LogEntries.AddAsync(new LogEntry()
            {
                Message = $"Granted {granted} for redeeming a redeemable code",
                TargetUserId = target.Id,
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
}
