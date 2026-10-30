namespace RevolutionaryWebApp.Server.Controllers;

using System;
using System.Linq;
using System.Threading.Tasks;
using Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models;
using Shared.Models;
using Shared.Models.Enums;

[ApiController]
[Route("api/v1/[controller]")]
[AuthorizeBasicAccessLevelFilter(RequiredAccess = GroupType.RestrictedUser)]
public class UserNotificationsController : Controller
{
    private readonly ApplicationDbContext database;

    public UserNotificationsController(ApplicationDbContext database)
    {
        this.database = database;
    }

    [HttpGet("unreadCount")]
    public async Task<ActionResult<UnreadNotificationCountDTO>> CountUnread()
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var count = await database.UserNotifications
            .CountAsync(n => n.UserId == userId && n.ReadAt == null && n.CombinedIntoId == null);
        return new UnreadNotificationCountDTO { Count = count };
    }

    [HttpGet]
    public async Task<ActionResult<UserNotificationDTO[]>> List([FromQuery] int offset = 0)
    {
        if (offset < 0)
            return BadRequest("Offset must be non-negative");

        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var rows = await database.UserNotifications.AsNoTracking()
            .Where(n => n.UserId == userId && n.CombinedIntoId == null)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip(offset).Take(50).ToListAsync();
        return rows.Select(notification => notification.GetDTO()).ToArray();
    }

    [HttpPost("{id:long}/read")]
    public async Task<IActionResult> MarkRead(long id)
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var notification = await database.UserNotifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (notification == null)
            return NotFound();

        var now = DateTime.UtcNow;
        notification.ReadAt ??= now;
        var children = await database.UserNotifications
            .Where(n => n.UserId == userId && n.CombinedIntoId == id && n.ReadAt == null).ToListAsync();
        foreach (var child in children)
        {
            child.ReadAt = now;
        }

        await database.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("readAll")]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = HttpContext.AuthenticatedUserOrThrow().Id;
        var now = DateTime.UtcNow;
        var unread = database.UserNotifications
            .Where(notification => notification.UserId == userId && notification.ReadAt == null);
        if (database.Database.IsRelational())
        {
            await unread.ExecuteUpdateAsync(update => update.SetProperty(notification => notification.ReadAt, now));
        }
        else
        {
            foreach (var notification in await unread.ToListAsync())
                notification.ReadAt = now;

            await database.SaveChangesAsync();
        }

        return NoContent();
    }
}
