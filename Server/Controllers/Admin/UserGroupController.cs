namespace RevolutionaryWebApp.Server.Controllers.Admin;

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/UserManagement/{id:long}/groups")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.Admin)]
public class UserGroupController(ILogger<UserGroupController> logger, NotificationsEnabledDb database,
    IBackgroundJobClient jobClient)
    : Controller
{
    [HttpGet]
    public async Task<ActionResult<List<UserGroupInfo>>> GetList([Required] long id)
    {
        var user = await database.Users.AsNoTracking().Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        // We need to go through this to get "implicit" groups resolved
        user.ProcessGroupDataFromLoadedGroups();

        var result = user.AccessCachedGroupsOrThrow().Groups.Select(g =>
        {
            // Convert the data
            var data = user.Groups.FirstOrDefault(u => u.Id == g);
            if (data != null)
                return data.GetInfo();

            // Or synthesise data for pseudo groups
            return new UserGroupInfo
            {
                Id = g,
                Name = g.ToString(),
            };
        });

        // And sort the results by group ID to be in the same order as the list of groups to add
        return result.OrderBy(g => g.Id).ToList();
    }

    [HttpPut("{groupId:int}")]
    public async Task<ActionResult<List<UserGroupInfo>>> AddGroup([Required] long id, [Required] int groupId)
    {
        var convertedId = (GroupType)groupId;

        // Disallow groups not managed through this
        if (convertedId <= GroupType.SystemOnly)
            return BadRequest("Cannot add system-assigned groups");

        var group = await database.UserGroups.FirstOrDefaultAsync(g => g.Id == convertedId);

        if (group == null)
        {
            return BadRequest("Unknown group type");
        }

        var user = await database.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        if (user.Groups.Any(g => g.Id == group.Id))
        {
            return Ok("User is already in group");
        }

        var performedBy = HttpContext.AuthenticatedUserOrThrow();

        user.Groups.Add(group);
        await database.AdminActions.AddAsync(new AdminAction($"User added to group {group.Id}")
        {
            PerformedById = performedBy.Id,
            TargetUserId = user.Id,
        });

        logger.LogInformation("User {Email} added to group {GroupId} by {Email2}", user.Email, group.Id,
            performedBy.Email);

        user.OnGroupsChanged(jobClient, false);

        await database.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{groupId:int}")]
    public async Task<ActionResult<List<UserGroupInfo>>> RemoveGroup([Required] long id, [Required] int groupId)
    {
        var convertedId = (GroupType)groupId;

        // Disallow groups not managed through this
        if (convertedId <= GroupType.SystemOnly)
            return BadRequest("Cannot remove system-assigned groups");

        var user = await database.Users.Include(u => u.Groups).FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound();

        var targetGroup = user.Groups.FirstOrDefault(g => g.Id == convertedId);

        if (targetGroup == null)
        {
            return BadRequest("User is not in the group");
        }

        var performedBy = HttpContext.AuthenticatedUserOrThrow();

        user.Groups.Remove(targetGroup);
        await database.AdminActions.AddAsync(new AdminAction($"User removed from group {targetGroup.Id}")
        {
            PerformedById = performedBy.Id,
            TargetUserId = user.Id,
        });

        logger.LogInformation("User {Email} removed from group {GroupId} by {Email2}", user.Email, targetGroup.Id,
            performedBy.Email);

        user.OnGroupsChanged(jobClient, false);

        await database.SaveChangesAsync();

        return Ok();
    }
}
