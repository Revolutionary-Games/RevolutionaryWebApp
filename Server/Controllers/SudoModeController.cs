namespace RevolutionaryWebApp.Server.Controllers;

using System.Threading.Tasks;
using Authorization;
using Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Services;
using Shared;
using Shared.Forms;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class SudoModeController(SudoModeService sudo) : Controller
{
    [HttpPost]
    [EnableRateLimiting(RateLimitCategories.LoginLimit)]
    public async Task<IActionResult> Enter(TwoFactorCodeForm data)
    {
        var session = HttpContext.AuthenticatedUserSession();
        if (session == null)
            return StatusCode(StatusCodes.Status403Forbidden, AppInfo.SudoModeWebsiteOnlyMessage);

        return await sudo.Enter(session, HttpContext.AuthenticatedUserOrThrow().Id, data.Code) ?
            Ok() :
            BadRequest("Two-factor authentication must be enabled. The code may be invalid or already used, " +
                "or there may have been too many attempts. Try a fresh code or try again later.");
    }
}
