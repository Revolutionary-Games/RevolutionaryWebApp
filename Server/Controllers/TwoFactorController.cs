namespace RevolutionaryWebApp.Server.Controllers;

using System.Collections.Generic;
using System.Threading.Tasks;
using Authorization;
using Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Services;
using Shared.Forms;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class TwoFactorController : Controller
{
    private readonly TwoFactorService twoFactor;

    public TwoFactorController(TwoFactorService twoFactor)
    {
        this.twoFactor = twoFactor;
    }

    [HttpGet]
    public async Task<ActionResult<TwoFactorStatusDTO>> Get()
    {
        if (HttpContext.AuthenticatedUserSession() == null)
            return Unauthorized("A browser login is required to manage two-factor authentication.");

        return await twoFactor.GetStatus(HttpContext.AuthenticatedUserOrThrow().Id);
    }

    [HttpPost("setup")]
    [EnableRateLimiting(RateLimitCategories.LoginLimit)]
    public async Task<ActionResult<TwoFactorSetupDTO>> Setup()
    {
        var session = HttpContext.AuthenticatedUserSession();
        if (session == null)
            return Unauthorized("A browser login is required to manage two-factor authentication.");

        return await twoFactor.CreateSetup(HttpContext.AuthenticatedUserOrThrow(), session);
    }

    [HttpPost("enable")]
    [EnableRateLimiting(RateLimitCategories.LoginLimit)]
    public async Task<ActionResult<List<string>>> Enable(TwoFactorCodeForm data)
    {
        var session = HttpContext.AuthenticatedUserSession();
        if (session == null)
            return Unauthorized("A browser login is required to manage two-factor authentication.");

        var codes = await twoFactor.Enable(HttpContext.AuthenticatedUserOrThrow().Id, session, data.Code);
        return codes == null ?
            BadRequest("Invalid or already used code, or too many attempts. Try again later.") :
            codes;
    }

    [HttpPost("disable")]
    [EnableRateLimiting(RateLimitCategories.LoginLimit)]
    public async Task<IActionResult> Disable(TwoFactorCodeForm data)
    {
        var session = HttpContext.AuthenticatedUserSession();
        if (session == null)
            return Unauthorized("A browser login is required to manage two-factor authentication.");

        return await twoFactor.Disable(HttpContext.AuthenticatedUserOrThrow().Id, session, data.Code) ?
            Ok("Two-factor authentication disabled.") :
            BadRequest("Invalid or already used code, or too many attempts. Try again later.");
    }

    [HttpPost("backup-codes")]
    [EnableRateLimiting(RateLimitCategories.LoginLimit)]
    public async Task<ActionResult<List<string>>> RegenerateBackupCodes(TwoFactorCodeForm data)
    {
        if (HttpContext.AuthenticatedUserSession() == null)
            return Unauthorized("A browser login is required to manage two-factor authentication.");

        var codes = await twoFactor.RegenerateBackupCodes(HttpContext.AuthenticatedUserOrThrow().Id, data.Code);
        return codes == null ?
            BadRequest("Invalid or already used code, or too many attempts. Try again later.") :
            Ok(codes);
    }
}
