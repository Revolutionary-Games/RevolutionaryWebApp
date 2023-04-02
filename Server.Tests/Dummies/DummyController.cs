namespace ThriveDevCenter.Server.Tests.Dummies;

using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Server.Authorization;
using Server.Models;
using Shared;
using Shared.Models;
using Shared.Models.Enums;
using SharedBase.ModelVerifiers;

[ApiController]
[Route("dummy")]
public class DummyController : Controller
{
    [HttpGet]
    public IActionResult GetBasic()
    {
        return NoContent();
    }

    [AuthorizeBasicAccessLevelFilter]
    [HttpGet("user")]
    public UserDTO GetLoggedIn()
    {
        var user = (User?)HttpContext.Items[AppInfo.CurrentUserMiddlewareKey];

        if (user == null || !user.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.User))
            throw new InvalidOperationException("user not retrieved or doesn't have access level");

        return user.GetDTO(RecordAccessLevel.Private);
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
    [HttpGet("restrictedUser")]
    public UserDTO GetRestricted()
    {
        var user = (User?)HttpContext.Items[AppInfo.CurrentUserMiddlewareKey];

        if (user == null || !user.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.RestrictedUser))
            throw new InvalidOperationException("user not retrieved or doesn't have access level");

        return user.GetDTO(RecordAccessLevel.Private);
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Developer)]
    [HttpGet("developer")]
    public UserDTO GetDeveloper()
    {
        var user = (User?)HttpContext.Items[AppInfo.CurrentUserMiddlewareKey];

        if (user == null || !user.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Developer))
            throw new InvalidOperationException("user not retrieved or doesn't have access level");

        return user.GetDTO(RecordAccessLevel.Private);
    }

    [AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
    [HttpGet("admin")]
    public UserDTO GetAdmin()
    {
        var user = (User?)HttpContext.Items[AppInfo.CurrentUserMiddlewareKey];

        if (user == null || !user.AccessCachedGroupsOrThrow().HasAccessLevel(GroupType.Admin))
            throw new InvalidOperationException("user not retrieved or doesn't have access level");

        return user.GetDTO(RecordAccessLevel.Admin);
    }

    [HttpPost]
    public IActionResult PostValidation([Required] [FromBody] DummyModel request)
    {
        return NoContent();
    }

    public class DummyModel
    {
        [Required]
        public string Field { get; set; } = string.Empty;

        [NotNullOrEmptyIf(PropertyMatchesValue = nameof(AValue), Value = "5")]
        public string? AnotherField { get; set; }

        public int AValue { get; set; }
    }
}
