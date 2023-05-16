namespace ThriveDevCenter.Server.Tests.Authorization.Tests;

using Microsoft.AspNetCore.Mvc;
using Server.Authorization;
using Shared.Models.Enums;

[ApiController]
[Route("accessFilterTest")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
public class AccessFilterTestController : Controller
{
    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.NotLoggedIn)]
    [HttpGet("nonWorkingNoLogin")]
    public IActionResult NoLoginFailTest()
    {
        return NoContent();
    }

    [HttpGet("restrictedUser")]
    public IActionResult RestrictedUser()
    {
        return NoContent();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    [HttpGet("user")]
    public IActionResult UserAccess()
    {
        return NoContent();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    [HttpGet("developer")]
    public IActionResult Developer()
    {
        return NoContent();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet("admin")]
    public IActionResult Admin()
    {
        return NoContent();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.SystemOnly)]
    [HttpGet("system")]
    public IActionResult SystemOnly()
    {
        return NoContent();
    }
}

[ApiController]
[Route("accessFilterTest2")]
public class AccessFilterTestNoRootAttributeController : Controller
{
    [HttpGet("noLogin")]
    public IActionResult NoLogin()
    {
        return NoContent();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.User)]
    [HttpGet("user")]
    public IActionResult UserAccess()
    {
        return NoContent();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    [HttpGet("developer")]
    public IActionResult Developer()
    {
        return NoContent();
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet("admin")]
    public IActionResult Admin()
    {
        return NoContent();
    }
}
