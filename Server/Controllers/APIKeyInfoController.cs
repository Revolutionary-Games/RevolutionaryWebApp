namespace RevolutionaryWebApp.Server.Controllers;

using System.Linq;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Shared;
using Shared.Models;

[ApiController]
[Route("api/v1/[controller]")]
[APIKeyScope]
public class APIKeyInfoController : Controller
{
    [HttpGet]
    public ActionResult<APIKeyInfo> Get()
    {
        if (!HttpContext.Items.TryGetValue(AppInfo.AuthenticationKeyMiddleWareKey, out var rawKey) ||
            rawKey is not APIKey key)
        {
            return Unauthorized();
        }

        Response.Headers.CacheControl = "no-store";
        return new APIKeyInfo
        {
            Key = key.GetDTO(),
            UserId = key.UserId,
            Groups = key.User.AccessCachedGroupsOrThrow().Groups.ToArray(),
        };
    }
}
