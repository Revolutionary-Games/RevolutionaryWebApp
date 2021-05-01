using Microsoft.AspNetCore.Mvc;

namespace ThriveDevCenter.Server.Tests.Dummies
{
    using System;
    using Server.Authorization;
    using Server.Models;
    using Shared;
    using Shared.Models;

    [ApiController]
    [Route("dummy")]
    public class DummyController : Controller
    {
        [HttpGet]
        public IActionResult GetBasic()
        {
            return NoContent();
        }

        [AuthorizeRoleFilter]
        [HttpGet("user")]
        public UserInfo GetLoggedIn()
        {
            var user = (User)HttpContext.Items[AppInfo.CurrentUserMiddlewareKey];

            if (user == null || !user.HasAccessLevel(UserAccessLevel.User))
                throw new InvalidOperationException("user not retrieved or doesn't have access level");

            return user.GetInfo(RecordAccessLevel.Private);
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Developer)]
        [HttpGet("developer")]
        public UserInfo GetDeveloper()
        {
            var user = (User)HttpContext.Items[AppInfo.CurrentUserMiddlewareKey];

            if (user == null || !user.HasAccessLevel(UserAccessLevel.Developer))
                throw new InvalidOperationException("user not retrieved or doesn't have access level");

            return user.GetInfo(RecordAccessLevel.Private);
        }

        [AuthorizeRoleFilter(RequiredAccess = UserAccessLevel.Admin)]
        [HttpGet("admin")]
        public UserInfo GetAdmin()
        {
            var user = (User)HttpContext.Items[AppInfo.CurrentUserMiddlewareKey];

            if (user == null || !user.HasAccessLevel(UserAccessLevel.Admin))
                throw new InvalidOperationException("user not retrieved or doesn't have access level");

            return user.GetInfo(RecordAccessLevel.Admin);
        }
    }
}
