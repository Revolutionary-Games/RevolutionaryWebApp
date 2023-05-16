namespace ThriveDevCenter.Server.Tests.Dummies;

using Microsoft.AspNetCore.Mvc;
using Server.Authorization;
using Shared.Models.Enums;

[ApiController]
[Route("groupFilterTest")]
[AuthorizeGroupMemberFilter(RequiredGroup = GroupType.Developer)]
public class GroupFilterTestController : Controller
{
    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.User)]
    [HttpGet("nonWorkingUser")]
    public IActionResult UserAccess()
    {
        return NoContent();
    }

    [HttpGet("developer")]
    public IActionResult Developer()
    {
        return NoContent();
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.Admin)]
    [HttpGet("admin")]
    public IActionResult Admin()
    {
        return NoContent();
    }

    [AuthorizeGroupMemberFilter(RequiredGroup = GroupType.SystemOnly)]
    [HttpGet("system")]
    public IActionResult SystemOnly()
    {
        return NoContent();
    }
}
