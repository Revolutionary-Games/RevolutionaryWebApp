using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Controllers
{
    using System;
    using System.ComponentModel.DataAnnotations;
    using System.Threading.Tasks;
    using Authorization;
    using Hubs;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;
    using Models;
    using Shared;
    using Shared.Models;
    using Shared.Notifications;

    [ApiController]
    [Route("api/v1/[controller]")]
    public class CodeRedeemController : Controller
    {
        private readonly ILogger<CodeRedeemController> logger;
        private readonly ApplicationDbContext database;
        private readonly IHubContext<NotificationsHub, INotifications> notifications;

        public CodeRedeemController(ILogger<CodeRedeemController> logger, ApplicationDbContext database,
            IHubContext<NotificationsHub, INotifications> notifications)
        {
            this.logger = logger;
            this.database = database;
            this.notifications = notifications;
        }

        [AuthorizeRoleFilter]
        [HttpPost]
        public async Task<IActionResult> Redeem([Required] RedeemCodeData data)
        {
            var target = HttpContext.Items[AppInfo.CurrentUserMiddlewareKey] as User;

            if (target == null)
                throw new Exception("User is null after authorization");

            if (string.IsNullOrEmpty(data.Code) || data.Code.Length < AppInfo.MinimumRedeemableCodeLength)
                return BadRequest("The code is too short");

            if (!Guid.TryParse(data.Code, out Guid parsedCode))
                return BadRequest("Invalid code format");

            var validCode = await database.RedeemableCodes.FirstOrDefaultAsync(c => c.Id == parsedCode);

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
                    if(target.Admin == true)
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
                TargetUser = target,
            });

            await database.SaveChangesAsync();

            logger.LogInformation("Code: {Code} has been redeemed by {Email}, granting: {Granted}", data.Code,
                target.Email, granted);

            var idStr = Convert.ToString(target.Id);

            await notifications.Clients.Group(NotificationGroups.UserUpdatedPrefix + idStr).ReceiveNotification(new UserUpdated
            {
                // Private is safe here as only admins and the user itself can join this group
                Item = target.GetInfo(RecordAccessLevel.Private)
            });

            await notifications.Clients.Group(NotificationGroups.UserUpdatedPrefixAdminInfo + idStr).ReceiveNotification(new UserUpdated
            {
                Item = target.GetInfo(RecordAccessLevel.Admin)
            });

            return Ok($"You have been granted: {granted}");
        }

        [NonAction]
        private IActionResult GetAlreadyGotResult()
        {
            return Conflict("You already have the resource you were trying to redeem");
        }
    }
}
